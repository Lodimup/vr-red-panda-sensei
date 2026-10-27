using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools.Schema;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Integration.ReviewFinal4;

/// <summary>
/// S133: the arguments of a function call in the history that nest deeper than the tool-argument parser takes (32 levels, the arguments
/// object being level 1, S21) are replaced by an empty object with one <c>engine:outbound_content</c> trace line, on the engine-built
/// client and on a host client alike. Such a call was already answered <c>invalid_arguments</c>, so nothing executable is lost; the
/// adapter would otherwise have failed on one nested past 64. Invented values, loopback server only.
/// </summary>
public sealed class ArgumentDepthTests
{
    /// <summary>An element of <paramref name="arrays"/> nested arrays around a zero.</summary>
    private static JsonElement Nested(int arrays)
    {
        var raw = new string('[', arrays) + "0" + new string(']', arrays);
        using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = arrays + 2 });
        return doc.RootElement.Clone();
    }

    /// <summary>Arguments whose document (the object included) is <paramref name="documentDepth"/> levels deep.</summary>
    private static Dictionary<string, object?> Arguments(int documentDepth) => new() { ["x"] = Nested(documentDepth - 1) };

    private static ChatMessage CallMessage(IDictionary<string, object?> arguments, string callId = "c1") => new(ChatRole.Assistant,
        new List<AIContent> { new TextContent("answer"), new FunctionCallContent(callId, "inspect_scene", arguments) { InformationalOnly = true } });

    private static int Outbound(RecordingTraceSink trace) =>
        trace.Entries.Count(e => e.Tool == "engine:outbound_content" && e.ErrorCode == "outbound_content_removed");

    /// <summary>Delegates to the real adapter but answers its first request with an assistant message that holds one call.</summary>
    private sealed class CallingWireClient(IChatClient inner, IDictionary<string, object?> arguments) : DelegatingChatClient(inner)
    {
        private int _calls;

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _calls) == 1 ? Task.FromResult(new ChatResponse(CallMessage(arguments))) : base.GetResponseAsync(messages, options, cancellationToken);
    }

    [Theory]
    [InlineData("host", 32, false)]
    [InlineData("host", 33, true)]
    [InlineData("host", 70, true)]
    [InlineData("host", 1_500, true)]
    [InlineData("wire", 32, false)]
    [InlineData("wire", 33, true)]
    [InlineData("wire", 70, true)]
    [InlineData("wire", 1_500, true)]
    public async Task A_call_nested_past_the_tool_argument_depth_goes_back_with_empty_arguments_and_one_trace(string path, int depth, bool replaced)
    {
        var arguments = Arguments(depth);
        var trace = new RecordingTraceSink();

        if (path == "host")
        {
            var chat = new ScriptedChatClient().Enqueue((_, _) => Task.FromResult(new ChatResponse(CallMessage(arguments)))).EnqueueText("done");
            await using var t = await TestEngine.CreateAsync(chat: chat, trace: trace);
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);

            Assert.Equal(2, chat.RequestCount);
            var call = chat.Requests[1].Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
            Assert.Equal("c1", call.CallId);
            Assert.Equal("inspect_scene", call.Name);
            if (replaced) Assert.Empty(call.Arguments!);
            else Assert.Equal(Nested(depth - 1).GetRawText(), Assert.IsType<JsonElement>(call.Arguments!["x"]).GetRawText());
        }
        else
        {
            using var server = new FakeChatServer().EnqueueText("done");
            using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "argument-depth-example", "fake-model", server.BaseUrl));
            using var chat = new CallingWireClient(wire, arguments);
            await using var t = await TestEngine.CreateAsync(chatClient: chat, trace: trace);
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);

            var request = Assert.Single(server.Requests);
            var sent = request.Json.GetProperty("messages").EnumerateArray().Single(m => m.TryGetProperty("tool_calls", out _))
                .GetProperty("tool_calls").EnumerateArray().Single().GetProperty("function");
            Assert.Equal("inspect_scene", sent.GetProperty("name").GetString());
            var argumentsText = sent.GetProperty("arguments").GetString()!;
            if (replaced) Assert.Equal("{}", argumentsText);
            else Assert.Equal(depth - 1, Regex.Matches(argumentsText, @"\[").Count);
        }

        Assert.Equal(replaced ? 1 : 0, Outbound(trace));
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, false)]
    [InlineData(32, false)]
    [InlineData(33, true)]
    [InlineData(34, true)]
    public void The_replacement_boundary_is_the_tool_argument_parsers_boundary(int depth, bool replaced)
    {
        var text = JsonSerializer.Serialize(Arguments(depth), new JsonSerializerOptions { MaxDepth = depth + 8 });
        var notes = new List<string>();
        var message = CallMessage(Arguments(depth));

        var sent = OutboundMessages.Sanitized(new[] { message }, Secrets.None, notes).Single().Contents.OfType<FunctionCallContent>().Single();

        // The parser the engine answers calls with takes exactly what the sanitizer keeps.
        Assert.Equal(!replaced, ArgumentSchema.TryParse(text, out _));
        Assert.Equal(replaced, sent.Arguments!.Count == 0);
        Assert.Equal(replaced ? 1 : 0, notes.Count);
        Assert.Equal(32, ArgumentSchema.MaxArgumentDepth);
    }

    [Fact]
    public void Nesting_made_of_dictionaries_lists_and_elements_is_counted_the_same_way()
    {
        object Lists(int levels) { object value = 0; for (var i = 0; i < levels; i++) value = new List<object?> { value }; return value; }
        object Dictionaries(int levels) { object value = 0; for (var i = 0; i < levels; i++) value = new Dictionary<string, object?> { ["k"] = value }; return value; }
        // Each at exactly 32 levels (the arguments object is level 1): kept.
        var atTheLimit = new Dictionary<string, object?> { ["a"] = Lists(31), ["b"] = new List<object?> { Dictionaries(30), Nested(30) } };
        // Each alone one level past it: replaced.
        var calls = new[]
        {
            CallMessage(atTheLimit, "c1"),
            CallMessage(new Dictionary<string, object?> { ["a"] = Lists(32) }, "c2"),
            CallMessage(new Dictionary<string, object?> { ["b"] = new List<object?> { Dictionaries(31) } }, "c3"),
            CallMessage(new Dictionary<string, object?> { ["b"] = new List<object?> { Nested(31) } }, "c4"),
        };
        var notes = new List<string>();

        var sent = OutboundMessages.Sanitized(calls, Secrets.None, notes).SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();

        Assert.NotEmpty(sent[0].Arguments!);
        Assert.All(sent.Skip(1), call => Assert.Empty(call.Arguments!));
        Assert.Equal(new[] { "c1", "c2", "c3", "c4" }, sent.Select(c => c.CallId));
        Assert.All(sent, call => Assert.Equal("inspect_scene", call.Name));
        Assert.All(sent, call => Assert.True(call.InformationalOnly));
        Assert.Equal(3, notes.Count);
    }

    [Fact]
    public void Two_over_deep_calls_in_one_request_are_one_trace_line()
    {
        var notes = new List<string>();
        var messages = new[] { CallMessage(Arguments(40), "c1"), CallMessage(Arguments(50), "c2") };

        var sent = OutboundMessages.Sanitized(messages, Secrets.None, notes).SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();

        Assert.All(sent, call => Assert.Empty(call.Arguments!));
        Assert.Equal(2, notes.Count);
        Assert.Single(notes.Distinct());
    }

    [Fact]
    public void Arguments_over_the_value_cap_keep_their_own_replacement_and_note()
    {
        var notes = new List<string>();
        var big = new Dictionary<string, object?> { ["x"] = Enumerable.Range(0, 10_001).Select(i => (object?)i).ToList() };

        var sent = OutboundMessages.Sanitized(new[] { CallMessage(big) }, Secrets.None, notes).Single().Contents.OfType<FunctionCallContent>().Single();

        Assert.Empty(sent.Arguments!);
        Assert.Equal("arguments over 10000 values (replaced by an empty object)", Assert.Single(notes));
    }

    [Fact]
    public void A_secret_in_arguments_that_are_kept_is_still_cleaned()
    {
        const string secret = "argument-depth-secret-ZX42";
        var arguments = new Dictionary<string, object?> { ["x"] = Nested(5), ["note"] = secret };

        var sent = OutboundMessages.Sanitized(new[] { CallMessage(arguments) }, new Secrets(new[] { secret }), new List<string>())
            .Single().Contents.OfType<FunctionCallContent>().Single();

        Assert.Equal("[redacted]", sent.Arguments!["note"]);
    }
}

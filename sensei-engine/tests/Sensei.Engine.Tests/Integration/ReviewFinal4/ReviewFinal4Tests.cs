using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Integration.ReviewFinal4;

/// <summary>S133 counterexamples. Only invented values, scripted clients and loopback HTTP are used.</summary>
public sealed class ReviewFinal4Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F01_wire_serialization_failure_must_use_the_single_rejection_boundary(bool statusException)
    {
        using var server = new FakeChatServer().EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "review4-wire-example", "fake-model", server.BaseUrl));
        using var chat = new SeededWireClient(wire, new ThrowingValue(statusException));
        Assert.NotNull(chat.GetService(typeof(OutboundGuard)));
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(chatClient: chat, trace: trace);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        var turn = await t.Engine.RunTurnAsync("two");
        Assert.Empty(server.Requests);
        var count = trace.Entries.Count(e => e.ErrorCode == "outbound_rejected");
        Assert.True(turn.Status == TurnStatuses.Failed && turn.ErrorCode == TurnErrorCodes.ProviderError
            && turn.ErrorDetail == "outbound request rejected: not_serializable" && count == 1,
            $"Expected provider_error/not_serializable and one rejection trace; status={turn.Status}; code={turn.ErrorCode}; detail={turn.ErrorDetail}; rejections={count}; requests={server.Requests.Count}");
        var entry = Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
        Assert.Equal("engine:outbound_rejected", entry.Tool);
        Assert.Equal("request not sent: not_serializable", entry.Arguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void F02_a_clr_backed_json_value_below_the_value_cap_must_survive(bool hostGuard)
    {
        var node = WrappedNode(1500);
        // This is writable JSON with 1,502 values. Raising the framework's default depth is sufficient to write it.
        var json = node.ToJsonString(new JsonSerializerOptions { MaxDepth = 20_001, TypeInfoResolver = new DefaultJsonTypeInfoResolver() });
        Assert.Equal(1_502, ValueCount(json));
        if (hostGuard)
        {
            var messages = new List<ChatMessage> { ResultMessage(node) };
            using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
            Assert.Same(messages, guard.Guarded(messages));
        }
        else
        {
            var clean = JsonRedaction.SanitizedValue(node, Secrets.None, out var oversized);
            Assert.False(oversized);
            Assert.Equal(json, Assert.IsType<JsonElement>(clean).GetRawText());
        }
    }

    [Fact]
    public void F02_a_clr_backed_oversized_json_value_argument_must_take_the_whole_replacement_path()
    {
        var node = WrappedNode(1500, 9000);
        Assert.Equal(10_501, ValueCount(node.ToJsonString(new JsonSerializerOptions { MaxDepth = 20_001, TypeInfoResolver = new DefaultJsonTypeInfoResolver() })));
        var call = new FunctionCallContent("c", "inspect_scene", new Dictionary<string, object?> { ["extra"] = node });
        var notes = new List<string>();
        var sent = OutboundMessages.Sanitized(new[] { new ChatMessage(ChatRole.Assistant, new List<AIContent> { call }) }, Secrets.None, notes);
        Assert.Empty(sent.Single().Contents.OfType<FunctionCallContent>().Single().Arguments!);
        Assert.Single(notes);
    }

    [Fact]
    public async Task F02_a_clr_backed_under_cap_json_value_must_not_break_the_next_turn()
    {
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(ResultMessage(WrappedNode(1500)))))
            .EnqueueText("done");
        await using var t = await TestEngine.CreateAsync(chat: chat);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        var turn = await t.Engine.RunTurnAsync("two");
        Assert.True(turn.Status == TurnStatuses.Completed,
            $"Valid 1502-value history rejected; status={turn.Status}; detail={turn.ErrorDetail}; requests={chat.RequestCount}");
        Assert.Equal(2, chat.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F03_empty_instructions_still_count_as_one_handed_over_string(bool streaming)
    {
        var inner = new ScriptedChatClient().EnqueueText("done");
        using var guard = new SerializedRequestGuard(inner, Secrets.None);
        var messages = TextMessages(20_000);
        var error = await Record.ExceptionAsync(() => InvokeAsync(guard, messages, new ChatOptions { Instructions = "" }, streaming));
        var handedOver = inner.RequestCount > 0 ? inner.Requests[0].Options?.Instructions : null;
        Assert.True(error is OutboundRejectedException && inner.RequestCount == 0,
            $"Expected too_many_values and no request; error={error?.GetType().Name ?? "none"}; requests={inner.RequestCount}; instructions={(handedOver is null ? "null" : "empty string")}");
        Assert.Equal("too_many_values", ((OutboundRejectedException)error!).Reason);
    }

    internal static List<ChatMessage> TextMessages(int count) => new() { new(ChatRole.User,
        Enumerable.Range(0, count).Select(_ => (AIContent)new TextContent("x")).ToList()) };

    internal static async Task InvokeAsync(IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions options, bool streaming)
    {
        if (streaming) { await foreach (var _ in client.GetStreamingResponseAsync(messages, options)) { } }
        else await client.GetResponseAsync(messages, options);
    }

    internal static ChatMessage ResultMessage(object? value) => new(ChatRole.Assistant,
        new List<AIContent> { new TextContent("answer"), new FunctionResultContent("prior", value) });

    internal static JsonValue WrappedNode(int depth, int leaves = 1)
    {
        var raw = new string('[', depth) + string.Join(',', Enumerable.Repeat("0", leaves)) + new string(']', depth);
        using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 20_001 });
        return JsonValue.Create(new WrappedJson(doc.RootElement.Clone()))!;
    }

    internal static int ValueCount(string json)
    {
        Assert.Equal(JsonWalkStatus.Ok, JsonRedaction.SanitizeJson(System.Text.Encoding.UTF8.GetBytes(json), s => s, 20_000,
            out _, out var values, out _));
        return values;
    }

    public sealed record WrappedJson(JsonElement Value);
    public sealed class ThrowingValue(bool statusException = false)
    {
        public string Text => throw (statusException
            ? new HttpRequestException("foreign serialization text", null, HttpStatusCode.Unauthorized)
            : new InvalidOperationException("foreign serialization text"));
    }

    /// <summary>Simulates a response-cache wrapper over the real adapter. GetService forwards the real byte guard, as documented.</summary>
    internal sealed class SeededWireClient(IChatClient inner, object value) : DelegatingChatClient(inner)
    {
        private int _calls;
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                return Task.FromResult(new ChatResponse(new List<ChatMessage> { new(ChatRole.Assistant, "answer"),
                    new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("prior", value) }) }));
            return base.GetResponseAsync(messages, options, cancellationToken);
        }
    }
}

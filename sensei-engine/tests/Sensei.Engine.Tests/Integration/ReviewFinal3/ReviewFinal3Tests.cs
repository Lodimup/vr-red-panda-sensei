using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Integration.ReviewFinal3;

/// <summary>Acceptance counterexamples against bf8592c. No real model or external endpoint is used.</summary>
public sealed class ReviewFinal3Tests
{
    private static JsonElement DeepElement(int depth) => JsonDocument.Parse(
        new string('[', depth) + "0" + new string(']', depth),
        new JsonDocumentOptions { MaxDepth = 20_001 }).RootElement.Clone();

    private static JsonNode DeepNode(int depth, int leaves = 1)
    {
        var raw = new string('[', depth) + string.Join(',', Enumerable.Repeat("0", leaves)) + new string(']', depth);
        return JsonNode.Parse(raw, documentOptions: new JsonDocumentOptions { MaxDepth = 20_001 })!;
    }

    private static ChatMessage ResultMessage(object? value) => new(ChatRole.Assistant,
        new List<AIContent> { new TextContent("answer"), new FunctionResultContent("prior", value) });

    [Theory]
    [InlineData(65)]
    [InlineData(1100)]
    public void F01_under_cap_json_elements_must_survive_the_host_guard(int depth)
    {
        var messages = new List<ChatMessage> { ResultMessage(DeepElement(depth)) };
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
        Assert.Same(messages, guard.Guarded(messages));
    }

    [Fact]
    public async Task F01_a_deep_result_in_history_must_not_fail_the_next_turn()
    {
        var original = DeepElement(70);
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(ResultMessage(original))))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);

        var turn = await t.Engine.RunTurnAsync("two");

        Assert.True(turn.Status == TurnStatuses.Completed,
            $"Valid 71-value history rejected: status={turn.Status}; detail={turn.ErrorDetail}; requests={chat.RequestCount}");
        Assert.Equal(2, chat.RequestCount);
        Assert.Equal(original.GetRawText(), Assert.IsType<JsonElement>(ResultsIn(chat.Requests[1]).Single().Result).GetRawText());
    }

    [Fact]
    public void F02_an_under_cap_json_node_must_not_be_silently_replaced()
    {
        var result = JsonRedaction.SanitizedValue(DeepNode(1100), Secrets.None, out var oversized);

        Assert.False(oversized);
        Assert.True(result is JsonElement, $"A 1101-value node was silently replaced with {result}");
    }

    [Fact]
    public void F02_an_oversized_deep_json_node_argument_must_replace_the_whole_arguments_and_be_traced()
    {
        // 1,100 array values + 9,000 number values = 10,100 values, before the arguments object.
        var call = new FunctionCallContent("c", "inspect_scene", new Dictionary<string, object?> { ["extra"] = DeepNode(1100, 9000) });
        var notes = new List<string>();
        var sent = OutboundMessages.Sanitized(new[] { new ChatMessage(ChatRole.Assistant, new List<AIContent> { call }) }, Secrets.None, notes);
        var arguments = sent.Single().Contents.OfType<FunctionCallContent>().Single().Arguments!;

        Assert.True(arguments.Count == 0 && notes.Count == 1,
            $"Expected empty arguments and one replacement note; keys={string.Join(',', arguments.Keys)}; notes={notes.Count}");
    }

    [Fact]
    public async Task F02_an_oversized_deep_json_node_result_replacement_must_be_traced()
    {
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(ResultMessage(DeepNode(1100, 9000)))))
            .EnqueueText("done");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);
        Assert.Equal("[oversized value]", ResultsIn(chat.Requests[1]).Single().Result);

        Assert.Single(trace.Entries, e => e.Tool == "engine:outbound_content" && e.ErrorCode == "outbound_content_removed");
    }

    [Fact]
    public async Task F03_text_values_must_count_toward_the_whole_request_cap()
    {
        var contents = Enumerable.Range(0, 20_001).Select(_ => (AIContent)new TextContent("answer")).ToList();
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))))
            .EnqueueText("never reached");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);

        var turn = await t.Engine.RunTurnAsync("two");

        var sentTexts = chat.RequestCount > 1 ? chat.Requests[1].Messages.SelectMany(m => m.Contents).OfType<TextContent>().Count() : 0;
        Assert.True(turn.Status == TurnStatuses.Failed,
            $"Expected rejection above 20000 values; status={turn.Status}; requests={chat.RequestCount}; text_values_sent={sentTexts}");
        Assert.Equal("outbound request rejected: too_many_values", turn.ErrorDetail);
        Assert.Equal(1, chat.RequestCount);
        Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
    }

    [Fact]
    public async Task F03_streaming_text_values_must_count_toward_the_whole_request_cap()
    {
        var messages = Enumerable.Range(0, 20_001).Select(_ => new ChatMessage(ChatRole.User, "hello")).ToList();
        var inner = new ScriptedChatClient().EnqueueText("done");
        using var guard = new SerializedRequestGuard(inner, Secrets.None);
        var error = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in guard.GetStreamingResponseAsync(messages)) { }
        });

        Assert.True(error is OutboundRejectedException && inner.RequestCount == 0,
            $"Expected too_many_values without passing to client; error={error?.GetType().Name ?? "none"}; requests={inner.RequestCount}; text_values={messages.Count}");
        Assert.Equal("too_many_values", ((OutboundRejectedException)error!).Reason);
    }

    [Theory]
    [InlineData("disposed_element")]
    [InlineData("throwing_enumerable")]
    public async Task F04_unwritable_values_must_use_the_outbound_rejection_contract(string shape)
    {
        object value;
        if (shape == "disposed_element")
        {
            var doc = JsonDocument.Parse("{\"a\":1}");
            value = doc.RootElement;
            doc.Dispose();
        }
        else value = new BrokenEnumerable();
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(ResultMessage(value))))
            .EnqueueText("never reached");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);

        var turn = await t.Engine.RunTurnAsync("two");

        Assert.Equal(TurnStatuses.Failed, turn.Status);
        Assert.Equal(TurnErrorCodes.ProviderError, turn.ErrorCode);
        Assert.Equal(1, chat.RequestCount);
        var rejections = trace.Entries.Where(e => e.ErrorCode == "outbound_rejected").ToList();
        Assert.True(turn.ErrorDetail == "outbound request rejected: not_serializable" && rejections.Count == 1,
            $"Expected not_serializable and one rejection trace; detail={turn.ErrorDetail}; rejection_traces={rejections.Count}");
        Assert.Equal("engine:outbound_rejected", rejections[0].Tool);
        Assert.Equal("request not sent: not_serializable", rejections[0].Arguments);
    }

    private sealed class BrokenEnumerable : IEnumerable
    {
        public IEnumerator GetEnumerator() => throw new InvalidOperationException("cannot enumerate");
    }
}

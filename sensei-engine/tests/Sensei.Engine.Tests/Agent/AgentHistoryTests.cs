using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Agent;

public sealed class AgentHistoryTests
{
    // ---------------------------------------------------------------- S65 last 12 learner turns

    [Fact]
    public async Task S65_history_keeps_the_last_12_learner_turns_in_every_request()
    {
        var chat = new ScriptedChatClient();
        for (var i = 0; i < 15; i++) chat.EnqueueText($"answer {i}");
        await using var t = await EngineAsync(chat);

        for (var i = 0; i < 15; i++) await t.Engine.RunTurnAsync($"turn {i}");

        for (var k = 0; k < 15; k++)
            Assert.Equal(Math.Min(k + 1, 12), UserMessages(chat.Requests[k]).Count);
        var last = UserMessages(chat.Requests[14]);
        Assert.Contains("learner_transcript: turn 14", last.Last().Text);
        Assert.Contains("learner_transcript: turn 3", last.First().Text);
        Assert.DoesNotContain(last, m => m.Text.Contains("learner_transcript: turn 2\n"));
    }

    [Fact]
    public async Task S65_the_cut_falls_at_a_learner_message_and_never_separates_a_call_from_its_result()
    {
        var chat = new ScriptedChatClient();
        for (var i = 0; i < 14; i++)
        {
            chat.EnqueueFunctionCalls(($"c{i}", "test_probe", ProbeJson("ok", "x")));
            chat.EnqueueText($"answer {i}");
        }
        await using var t = await EngineAsync(chat);

        for (var i = 0; i < 14; i++) await t.Engine.RunTurnAsync($"turn {i}");

        var request = chat.Requests[chat.RequestCount - 1];
        Assert.Equal(ChatRole.User, request.Messages[0].Role);
        Assert.Equal(12, UserMessages(request).Count);
        AssertNoUnansweredCalls(request);
        for (var i = 0; i < request.Messages.Count; i++)
            if (request.Messages[i].Role == ChatRole.Tool) Assert.Contains(request.Messages[i - 1].Contents, c => c is FunctionCallContent);
    }

    [Fact]
    public async Task S65_history_is_empty_again_after_a_reset()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "a"))).EnqueueText("a").EnqueueText("b");
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("one");
        await t.Engine.ResetProfileAsync();

        await t.Engine.RunTurnAsync("two");

        var request = chat.Requests.Last();
        Assert.Single(request.Messages);
        Assert.Contains("learner_transcript: two", request.Messages[0].Text);
    }

    // ---------------------------------------------------------------- S65 settling

    [Fact]
    public async Task S65_a_call_left_unanswered_by_a_cancelled_turn_gets_a_synthetic_cancelled_result_next_turn()
    {
        var probe = new ProbeTool();
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("slow", delayMs: 30000))).EnqueueText("fine");
        await using var t = await EngineAsync(chat, probes: false, more: probe);
        var first = t.Engine.RunTurnAsync("start");
        await WaitUntilAsync(() => probe.Executions == 1);
        t.Engine.CancelGeneration();
        Assert.Equal(TurnStatuses.Cancelled, (await first).Status);

        await t.Engine.RunTurnAsync("again");

        var request = chat.Requests[1];
        var callIndex = request.Messages.ToList().FindIndex(m => m.Contents.OfType<FunctionCallContent>().Any(c => c.CallId == "c1"));
        Assert.True(callIndex >= 0, "the earlier call is part of the history");
        var after = request.Messages[callIndex + 1];
        Assert.Equal(ChatRole.Tool, after.Role);
        var result = Assert.Single(after.Contents.OfType<FunctionResultContent>());
        Assert.Equal("c1", result.CallId);
        var json = ResultJson(result);
        Assert.Equal("cancelled", json.GetProperty("status").GetString());
        Assert.Equal("cancelled", json.GetProperty("error_code").GetString());
        Assert.Equal(2, json.EnumerateObject().Count());
    }

    [Fact]
    public async Task S65_the_provider_never_receives_an_unanswered_tool_call()
    {
        var probe = new ProbeTool();
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("slow", delayMs: 30000)), ("c2", "test_probe", ProbeJson("ok", "b")))
            .EnqueueFunctionCalls(("c3", "test_probe", ProbeJson("ok", "c")))
            .EnqueueText("fine");
        await using var t = await EngineAsync(chat, probes: false, more: probe);
        var first = t.Engine.RunTurnAsync("start");
        await WaitUntilAsync(() => probe.Executions == 1);
        t.Engine.CancelGeneration();
        await first;
        await t.Engine.RunTurnAsync("again");
        await t.Engine.RunTurnAsync("and again");

        foreach (var request in chat.Requests) AssertNoUnansweredCalls(request);
    }

    [Fact]
    public async Task S65_a_provider_failure_after_tool_calls_leaves_the_calls_settled_for_the_next_turn()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("spawn")))
            .EnqueueError(new InvalidOperationException("provider exploded"))
            .EnqueueText("recovered");
        await using var t = await EngineAsync(chat);

        var failed = await t.Engine.RunTurnAsync("one");
        await t.Engine.RunTurnAsync("two");

        Assert.Equal(TurnStatuses.Failed, failed.Status);
        Assert.Equal(1, t.Engine.GetScene().Revision);
        AssertNoUnansweredCalls(chat.Requests.Last());
        Assert.Contains(ResultsIn(chat.Requests.Last()), r => r.CallId == "c1");
    }

    // ---------------------------------------------------------------- unit: settler and reducer

    private static ChatMessage Assistant(params string[] callIds) =>
        new(ChatRole.Assistant, callIds.Select(id => (AIContent)new FunctionCallContent(id, "tool", new Dictionary<string, object?>())).ToList());

    private static ChatMessage ToolMessage(params string[] callIds) =>
        new(ChatRole.Tool, callIds.Select(id => (AIContent)new FunctionResultContent(id, "ok")).ToList());

    [Fact]
    public void S65_settler_leaves_a_history_without_open_calls_alone()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "u"), Assistant("a"), ToolMessage("a"), new(ChatRole.Assistant, "text") };
        Assert.Equal(0, HistorySettler.Settle(messages));
        Assert.Equal(4, messages.Count);
    }

    [Fact]
    public void S65_settler_answers_only_the_missing_calls_of_a_partly_answered_message()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "u"), Assistant("a", "b", "c"), ToolMessage("b"), new(ChatRole.User, "next") };
        Assert.Equal(2, HistorySettler.Settle(messages));

        Assert.Equal(5, messages.Count);
        Assert.Equal(ChatRole.Tool, messages[3].Role);
        Assert.Equal(new[] { "a", "c" }, messages[3].Contents.OfType<FunctionResultContent>().Select(r => r.CallId).ToArray());
        Assert.Equal(ChatRole.User, messages[4].Role);
    }

    [Fact]
    public void S65_settler_answers_a_call_at_the_very_end_of_the_history()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "u"), Assistant("a") };
        Assert.Equal(1, HistorySettler.Settle(messages));
        Assert.Equal(ChatRole.Tool, messages[2].Role);
    }

    [Fact]
    public void S65_settler_handles_several_open_messages_and_is_idempotent()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "u1"), Assistant("a"), new(ChatRole.User, "u2"), Assistant("b", "c") };
        Assert.Equal(3, HistorySettler.Settle(messages));
        Assert.Equal(0, HistorySettler.Settle(messages));
        Assert.Equal(6, messages.Count);
        Assert.Equal(new[] { ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User, ChatRole.Assistant, ChatRole.Tool }, messages.Select(m => m.Role).ToArray());
    }

    [Fact]
    public void S65_settler_result_is_the_cancelled_json_object()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "u"), Assistant("a") };
        HistorySettler.Settle(messages);
        var result = messages[2].Contents.OfType<FunctionResultContent>().Single();
        Assert.Equal("{\"status\":\"cancelled\",\"error_code\":\"cancelled\"}", ((JsonElement)result.Result!).GetRawText());
    }

    [Fact]
    public void S65_reducer_keeps_everything_while_there_are_few_learner_messages()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "1"), Assistant("a"), ToolMessage("a"), new(ChatRole.User, "2") };
        Assert.Equal(4, LearnerBoundaryReducer.Trim(messages, 12).Count);
        Assert.Equal(4, LearnerBoundaryReducer.Trim(messages, 2).Count);
    }

    [Fact]
    public void S65_reducer_cuts_at_the_learner_message_that_starts_the_kept_window()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "1"), Assistant("a"), ToolMessage("a"), new(ChatRole.Assistant, "x"),
            new(ChatRole.User, "2"), new(ChatRole.Assistant, "y"),
            new(ChatRole.User, "3"), Assistant("b"), ToolMessage("b"),
        };
        var kept = LearnerBoundaryReducer.Trim(messages, 2);
        Assert.Equal(5, kept.Count);
        Assert.Equal("2", kept[0].Text);
        Assert.Equal(ChatRole.User, kept[0].Role);
        Assert.Equal(ChatRole.Tool, kept[^1].Role);
    }

    [Fact]
    public async Task S65_reducer_implements_the_chat_reducer_contract()
    {
        var reducer = new LearnerBoundaryReducer(1);
        var reduced = (await reducer.ReduceAsync(new[] { new ChatMessage(ChatRole.User, "1"), new ChatMessage(ChatRole.Assistant, "x"), new ChatMessage(ChatRole.User, "2") }, CancellationToken.None)).ToList();
        Assert.Single(reduced);
        Assert.Equal("2", reduced[0].Text);
    }

    [Fact]
    public void S65_reducer_with_nothing_to_keep_returns_nothing()
    {
        Assert.Empty(LearnerBoundaryReducer.Trim(new List<ChatMessage> { new(ChatRole.User, "1") }, 0));
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertNoUnansweredCalls(RecordedChatRequest request)
    {
        var answered = ResultsIn(request).Select(r => r.CallId).ToHashSet();
        foreach (var call in CallsIn(request)) Assert.Contains(call.CallId, answered);
    }
}

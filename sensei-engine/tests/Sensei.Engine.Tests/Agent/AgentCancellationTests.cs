using Microsoft.Extensions.AI;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Agent;

public sealed class AgentCancellationTests
{
    /// <summary>A scripted answer that never arrives: it waits until its token is cancelled and reports that it saw the cancellation.</summary>
    private static Func<RecordedChatRequest, CancellationToken, Task<ChatResponse>> Hangs(TaskCompletionSource<bool> sawCancellation) =>
        async (_, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                sawCancellation.TrySetResult(true);
                throw;
            }
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "unreachable"));
        };

    [Fact]
    public async Task S66_cancel_generation_during_the_provider_request_cancels_the_request_and_returns_cancelled()
    {
        var saw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient();
        chat.Enqueue(Hangs(saw));
        await using var t = await EngineAsync(chat);
        var revision = t.Engine.GetScene().Revision;

        var turn = t.Engine.RunTurnAsync("water please");
        await WaitUntilAsync(() => chat.RequestCount == 1);
        t.Engine.CancelGeneration();
        var result = await turn.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(await saw.Task.WaitAsync(TimeSpan.FromSeconds(10)), "the provider request saw the cancellation");
        Assert.Equal(TurnStatuses.Cancelled, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Equal(revision, t.Engine.GetScene().Revision);
        Assert.Equal(1, result.Rounds);
    }

    [Fact]
    public async Task S66_run_turn_does_not_throw_when_cancelled()
    {
        var chat = new ScriptedChatClient();
        chat.Enqueue(Hangs(new TaskCompletionSource<bool>()));
        await using var t = await EngineAsync(chat);
        var turn = t.Engine.RunTurnAsync("hi");
        await WaitUntilAsync(() => chat.RequestCount == 1);
        t.Engine.CancelGeneration();
        var exception = await Record.ExceptionAsync(() => turn.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(exception);
    }

    [Fact]
    public async Task S66_later_tool_calls_of_that_generation_are_cancelled_until_the_next_turn()
    {
        var chat = new ScriptedChatClient();
        chat.Enqueue(Hangs(new TaskCompletionSource<bool>()));
        await using var t = await EngineAsync(chat);
        var turn = t.Engine.RunTurnAsync("hi");
        await WaitUntilAsync(() => chat.RequestCount == 1);
        t.Engine.CancelGeneration();
        await turn;

        var late = await t.Engine.InvokeToolAsync("test_probe", ProbeJson("ok", "x"));
        Assert.Equal("cancelled", late.Status);
        Assert.Equal("cancelled", late.ErrorCode);

        t.Engine.BeginTurn("fresh");
        var fresh = await t.Engine.InvokeToolAsync("test_probe", ProbeJson("ok", "x"));
        Assert.Equal("success", fresh.Status);
    }

    [Fact]
    public async Task S66_a_tool_in_flight_and_the_rest_of_the_response_are_cancelled()
    {
        var probe = new ProbeTool();
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("slow", delayMs: 30000)), ("c2", "test_probe", ProbeJson("ok", "b")))
            .EnqueueText("never used");
        await using var t = await EngineAsync(chat, probes: false, more: probe);

        var turn = t.Engine.RunTurnAsync("go");
        await WaitUntilAsync(() => probe.Executions == 1);
        t.Engine.CancelGeneration();
        var result = await turn.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(TurnStatuses.Cancelled, result.Status);
        Assert.Equal(new[] { "cancelled", "cancelled" }, result.ToolCalls.Select(c => c.Status).ToArray());
        Assert.Equal(1, probe.Executions);
        Assert.Equal(1, chat.RequestCount);
    }

    [Fact]
    public async Task S66_the_turn_after_a_cancelled_one_runs_normally()
    {
        var chat = new ScriptedChatClient();
        chat.Enqueue(Hangs(new TaskCompletionSource<bool>()));
        chat.EnqueueText("all good");
        await using var t = await EngineAsync(chat);
        var first = t.Engine.RunTurnAsync("one");
        await WaitUntilAsync(() => chat.RequestCount == 1);
        t.Engine.CancelGeneration();
        await first;

        var second = await t.Engine.RunTurnAsync("two");

        Assert.Equal(TurnStatuses.Completed, second.Status);
        Assert.Equal("all good", second.UnspokenText);
    }

    [Fact]
    public async Task S66_a_cancelled_caller_token_also_ends_the_turn_as_cancelled()
    {
        var saw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient();
        chat.Enqueue(Hangs(saw));
        await using var t = await EngineAsync(chat);
        using var cts = new CancellationTokenSource();

        var turn = t.Engine.RunTurnAsync("hi", cts.Token);
        await WaitUntilAsync(() => chat.RequestCount == 1);
        cts.Cancel();
        var result = await turn.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(TurnStatuses.Cancelled, result.Status);
        Assert.True(await saw.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task S66_a_cancelled_turn_reports_the_idle_state()
    {
        var events = new RecordingEvents();
        var chat = new ScriptedChatClient();
        chat.Enqueue(Hangs(new TaskCompletionSource<bool>()));
        await using var t = await EngineAsync(chat, events: events);
        var turn = t.Engine.RunTurnAsync("hi");
        await WaitUntilAsync(() => chat.RequestCount == 1);
        t.Engine.CancelGeneration();
        await turn;
        Assert.Equal(new[] { "thinking", "idle" }, events.SenseiStates.ToArray());
    }

    [Fact]
    public async Task S66_cancelling_when_no_turn_runs_changes_nothing()
    {
        var chat = new ScriptedChatClient().EnqueueText("ok");
        await using var t = await EngineAsync(chat);
        t.Engine.CancelGeneration();
        var result = await t.Engine.RunTurnAsync("hi");
        Assert.Equal(TurnStatuses.Completed, result.Status);
    }

    // ---------------------------------------------------------------- S68 one turn at a time

    [Fact]
    public async Task S68_a_new_turn_cancels_the_running_one_and_waits_for_it_to_settle()
    {
        var saw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient();
        chat.Enqueue(Hangs(saw));
        chat.EnqueueText("second answer");
        await using var t = await EngineAsync(chat);

        var first = t.Engine.RunTurnAsync("one");
        await WaitUntilAsync(() => chat.RequestCount == 1);
        var second = await t.Engine.RunTurnAsync("two");
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(saw.Task.IsCompleted);
        Assert.Equal(TurnStatuses.Cancelled, firstResult.Status);
        Assert.Equal(TurnStatuses.Completed, second.Status);
        Assert.Equal(2, chat.RequestCount);
        Assert.Equal(2, second.TurnId);
    }

    [Fact]
    public async Task S68_the_replacing_turn_is_told_the_previous_one_was_interrupted()
    {
        var chat = new ScriptedChatClient();
        chat.Enqueue(Hangs(new TaskCompletionSource<bool>()));
        chat.EnqueueText("ok");
        await using var t = await EngineAsync(chat);
        var first = t.Engine.RunTurnAsync("one");
        await WaitUntilAsync(() => chat.RequestCount == 1);
        await t.Engine.RunTurnAsync("two");
        await first;

        var block = LearnerBlock(chat.Requests[1]);
        Assert.Contains("previous_turn: it was interrupted before it finished", block);
        Assert.Contains("interrupted_learner_transcript: one", block);
        Assert.Contains("learner_transcript: two", block);
    }
}

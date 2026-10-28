using Microsoft.Extensions.AI;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.ReviewE1;

public sealed class F02Interruption
{
    [Fact]
    public async Task Interrupted_event_does_not_label_an_older_completed_utterance_interrupted()
    {
        var chat = new ScriptedChatClient().EnqueueText("ok").Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "never"));
        });
        await using var t = await EngineAsync(chat);
        Assert.Equal("completed", (await t.Engine.RunTurnAsync("earlier completed line")).Status);
        var eventTurn = t.Engine.RunPlayerEventTurnAsync("help_hint");
        await WaitUntilAsync(() => chat.RequestCount == 2);
        t.Engine.CancelGeneration();
        Assert.Equal("cancelled", (await eventTurn).Status);
        await t.Engine.RunTurnAsync("next line");
        var block = LearnerBlock(chat.Requests[2]);
        Assert.Contains("previous_turn: it was interrupted", block);
        Assert.DoesNotContain("interrupted_learner_transcript: earlier completed line", block);
    }
}

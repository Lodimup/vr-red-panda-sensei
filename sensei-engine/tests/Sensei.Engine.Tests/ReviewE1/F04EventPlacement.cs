using Microsoft.Extensions.AI;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.ReviewE1;

public sealed class F04EventPlacement
{
    [Theory]
    [InlineData("session_start", "The learner just started a session and placed the practice area. Greet them. For a new profile, ask what Japanese they already know. For a returning learner, offer to continue from their review targets.")]
    [InlineData("help_repeat", "The learner pressed the Repeat button. Replay your last utterance with replay_speech.")]
    [InlineData("area_placed", "The learner moved the practice area. The scene did not change. Do not respond unless something needs saying.")]
    public async Task Event_is_engine_owned_and_not_learner_data(string kind, string meaning)
    {
        var chat = new ScriptedChatClient();
        await using var t = await EngineAsync(chat);
        await t.Engine.RunPlayerEventTurnAsync(kind);
        var request = chat.Requests[0];
        var instructions = request.Options!.Instructions!;
        Assert.Contains($"## Current state (engine-owned, refreshed before every model request)\n- player event this turn: {kind}. {meaning}\n", instructions);
        Assert.Contains("- this is the first request of a player-event turn: do what the player event asks, all in this one response. Speak only if it calls for speech.", instructions);
        Assert.DoesNotContain("player event", LearnerBlock(request), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("learner_transcript:", LearnerBlock(request));
        Assert.DoesNotContain(meaning, LearnerBlock(request));
    }
    [Fact]
    public async Task Interrupted_event_is_named_in_the_next_turn()
    {
        var chat = new ScriptedChatClient().EnqueueText("ok").Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "never"));
        });
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("earlier line");
        var pending = t.Engine.RunPlayerEventTurnAsync("help_hint");
        await WaitUntilAsync(() => chat.RequestCount == 2);
        t.Engine.CancelGeneration();
        await pending;
        await t.Engine.RunTurnAsync("next line");
        Assert.Contains("interrupted_player_event: help_hint", LearnerBlock(chat.Requests[2]));
    }
}

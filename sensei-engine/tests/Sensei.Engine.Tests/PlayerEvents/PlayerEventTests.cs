using Sensei.Engine.Tests.Cli;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.PlayerEvents;

public sealed class PlayerEventTests
{
    [Fact]
    public async Task Invalid_events_do_not_start_a_turn()
    {
        var chat = new ScriptedChatClient();
        await using var t = await EngineAsync(chat);
        Assert.Equal(new[] { "session_start", "help_hint", "help_repeat", "help_english", "area_placed" }, SenseiEngine.PlayerEventKinds);
        foreach (var value in new string?[] { null, "", "SESSION_START", "other" })
        {
            var result = await t.Engine.RunPlayerEventTurnAsync(value!);
            Assert.Equal("failed", result.Status);
            Assert.Equal("invalid_player_event", result.ErrorCode);
        }
        Assert.Equal(0, chat.RequestCount);
        Assert.Equal("nothing_to_correct", (await t.Engine.CorrectLastTranscriptAsync("x")).ErrorCode);
        Assert.Equal(1, (await t.Engine.RunTurnAsync("hello")).TurnId);
    }

    [Theory]
    [InlineData("session_start", "The learner just started a session and placed the practice area. Greet them. For a new profile, ask what Japanese they already know. For a returning learner, offer to continue from their review targets.")]
    [InlineData("help_hint", "The learner pressed the Hint button. Give one hint for the active task with show_hint, or a useful phrase if there is no task.")]
    [InlineData("help_repeat", "The learner pressed the Repeat button. Replay your last utterance with replay_speech.")]
    [InlineData("help_english", "The learner asked for an English explanation of what is happening right now.")]
    [InlineData("area_placed", "The learner moved the practice area. The scene did not change. Do not respond unless something needs saying.")]
    public async Task Context_contains_event_meaning_and_correction_skips_event(string kind, string meaning)
    {
        var chat = new ScriptedChatClient();
        await using var t = await EngineAsync(chat);
        Assert.Equal("completed", (await t.Engine.RunPlayerEventTurnAsync(kind)).Status);
        Assert.Contains($"- player event this turn: {kind}. {meaning}", chat.Requests[0].Options!.Instructions); Assert.DoesNotContain(meaning, LearnerBlock(chat.Requests[0]));
        Assert.DoesNotContain("learner_transcript:", LearnerBlock(chat.Requests[0]));
        Assert.Equal("nothing_to_correct", (await t.Engine.CorrectLastTranscriptAsync("x")).ErrorCode);
    }

    [Fact]
    public async Task Correction_targets_real_utterance_before_event()
    {
        var chat = new ScriptedChatClient();
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("original");
        await t.Engine.RunPlayerEventTurnAsync("help_hint");
        var corrected = await t.Engine.CorrectLastTranscriptAsync("corrected");
        Assert.Equal("completed", corrected.Status);
        Assert.Contains("corrected from \"original\" to \"corrected\"", LearnerBlock(chat.Requests[2]));
    }

    [Fact]
    public async Task Synthetic_event_is_not_an_attempt_source()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "record_attempt", "{\"target\":{\"dimension\":\"production\",\"key\":\"event\"},\"transcript_excerpt\":\"player event\",\"communicative_success\":true,\"language_assessment\":\"correct\"}"));
        await using var t = await EngineAsync(chat);
        var result = await t.Engine.RunPlayerEventTurnAsync("help_hint");
        Assert.False(Assert.Single(result.ToolCalls).Result!.IsSuccess);
    }

    [Fact]
    public async Task Event_cancels_running_turn()
    {
        var chat = new ScriptedChatClient().Enqueue(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); throw new Exception(); });
        await using var t = await EngineAsync(chat);
        var first = t.Engine.RunTurnAsync("hello");
        await WaitUntilAsync(() => chat.RequestCount == 1);
        var second = await t.Engine.RunPlayerEventTurnAsync("help_hint");
        Assert.Equal("cancelled", (await first).Status);
        Assert.Equal("completed", second.Status);
    }

    [Fact]
    public async Task Cli_event_and_help()
    {
        var chat = new ScriptedChatClient();
        var result = await InProcessCli.ScriptAsync(new[] { "/help", "/event session_start" }, chat);
        Assert.Contains("/event <kind>", result.Text);
        Assert.Contains("- player event this turn: session_start. The learner just started a session and placed the practice area.", chat.Requests[0].Options!.Instructions); Assert.DoesNotContain("player event", LearnerBlock(chat.Requests[0]));
        Assert.Contains("completed", result.Text);
    }
}

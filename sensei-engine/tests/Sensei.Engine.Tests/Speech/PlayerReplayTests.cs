using Microsoft.Extensions.AI;
using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.Speech;

public sealed class PlayerReplayTests
{
    [Fact]
    public async Task Empty_history_does_not_start_a_turn_or_speak()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Events.Clear();
        var result = await t.Engine.ReplayLastSpeechAsync();
        Assert.Empty(t.Events.All);
        Assert.Equal(ToolErrorCodes.NothingToReplay, result.ErrorCode);
        Assert.Null(t.Core.CurrentTurn);
        Assert.Empty(t.Speech.Requests);
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task Replays_latest_with_new_id_support_pace_and_repetition()
    {
        await using var t = await NewAsync();
        StartTask(t, ("listening", "apple", null));
        await SpeakOne(t, "en", "first");
        await SpeakOne(t, "en", "second");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        var result = await t.Engine.ReplayLastSpeechAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal("second", t.Speech.Requests.Last().Segments.Single().Text);
        Assert.Equal("slow", t.Speech.Requests.Last().Pace);
        Assert.Equal("u3", DataString(result, "utterance_id"));
        Assert.Single(Assistance(t, "repetition"));
        Assert.True(History(t).Last().IsReplay);
    }
    [Fact]
    public async Task Replay_cancels_running_turn_without_another_model_request()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient().Enqueue(async (_, ct) =>
        {
            entered.SetResult(true);
            await Task.Delay(Timeout.Infinite, ct);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "unreachable"));
        });
        await using var t = await TestEngine.CreateAsync(chat: chat);
        t.Engine.BeginTurn("setup");
        await SpeakOne(t, "en", "water");
        var turn = t.Engine.RunTurnAsync("question");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var replay = await t.Engine.ReplayLastSpeechAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TurnStatuses.Cancelled, (await turn).Status);
        Assert.True(replay.IsSuccess);
        Assert.Equal(1, chat.RequestCount);
        Assert.Equal("water", t.Speech.Requests.Last().Segments.Single().Text);
    }
}

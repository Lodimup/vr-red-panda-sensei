using Microsoft.Extensions.AI;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.ReviewR4;

public class ReplayPauseTests
{
    [Fact]
    public async Task PauseCancellationPreventsReplayWaitingOnPriorTurn()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient().Enqueue(async (_, ct) => {
            entered.TrySetResult(true);
            await release.Task;
            ct.ThrowIfCancellationRequested();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "unreachable"));
        });
        await using var t = await TestEngine.CreateAsync(chat: chat);
        t.Engine.BeginTurn("setup");
        Assert.True((await SpeakOne(t, "en", "water")).IsSuccess);
        var turn = t.Engine.RunTurnAsync("question");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var replay = t.Engine.ReplayLastSpeechAsync();
        Assert.False(replay.IsCompleted);
        t.Engine.CancelGeneration(); // EngineHost.OnApplicationPause -> InterruptSpeech
        release.TrySetResult(true);
        await turn.WaitAsync(TimeSpan.FromSeconds(10));
        var result = await replay.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(t.Speech.Requests); // Only setup speech should have reached the output.
    }
}

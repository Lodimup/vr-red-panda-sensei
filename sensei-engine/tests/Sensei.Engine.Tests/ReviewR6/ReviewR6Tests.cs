using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.ReviewR6;

public class ReviewR6Tests
{
    [Fact]
    public async Task PauseBetweenHostEpochCheckAndEngineEntryCancelsQueuedRepeat()
    {
        await using var t = await NewAsync();
        Assert.True((await SpeakOne(t, "en", "water")).IsSuccess);
        var checkedEpoch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWorker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pauseCts = new CancellationTokenSource();
        var pauseEpoch = 0;
        var captured = Volatile.Read(ref pauseEpoch);
        var repeat = Task.Run(async () => {
            // Model EngineHost's epoch comparison immediately before its engine call.
            if (Volatile.Read(ref pauseEpoch) != captured) return;
            checkedEpoch.SetResult(true);
            await releaseWorker.Task; // deterministic preemption at the check/call boundary
            await t.Engine.ReplayLastSpeechAsync(ct: pauseCts.Token);
        });
        await checkedEpoch.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Interlocked.Increment(ref pauseEpoch); // OnApplicationPause(true)
        pauseCts.Cancel();                    // OnApplicationPause(true)
        t.Engine.CancelGeneration();          // InterruptSpeech()
        releaseWorker.SetResult(true);
        await repeat.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(t.Speech.Requests);
    }
}

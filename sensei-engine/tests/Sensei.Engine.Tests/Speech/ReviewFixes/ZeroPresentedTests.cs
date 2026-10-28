using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.Speech;

public sealed class ReviewS3ZeroPresentedTests
{
    [Fact]
    public async Task S89_explicit_zero_presented_does_not_create_history_or_audio_model()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        t.Speech.Outcome = Outcome(SpeechStatuses.Completed, 0);
        var r = await SpeakOne(t, "ja", "りんご");
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Empty(History(t));
        Assert.Empty(Assistance(t));
        Assert.Equal(0, SpokenCharacters(t));
    }

    [Fact]
    public async Task S90_explicit_zero_presented_replay_does_not_record_repetition()
    {
        await using var t = await NewAsync();
        StartTask(t, ("listening", "apple", null));
        await SpeakOne(t, "en", "hello");
        t.Speech.Outcome = Outcome(SpeechStatuses.Completed, 0);
        var r = await Replay(t);
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Empty(Assistance(t, AssistanceKinds.Repetition));
        Assert.Single(History(t));
    }
}

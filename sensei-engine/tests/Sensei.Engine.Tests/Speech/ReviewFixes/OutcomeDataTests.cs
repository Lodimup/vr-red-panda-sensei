using Sensei.Engine.Speech;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.Speech;

public sealed class ReviewS3OutcomeDataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S37d_S90_partial_cancel_returns_the_utterance_id_and_outcome(bool replay)
    {
        await using var t = await NewAsync();
        if (replay) await Speak(t, Segment("en", "one"), Segment("en", "two"));
        t.Speech.Outcome = Outcome(SpeechStatuses.Cancelled, 1);
        var r = replay ? await Replay(t) : await Speak(t, Segment("en", "one"), Segment("en", "two"));
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        var entry = History(t).Last();
        Assert.Equal(SpeechStatuses.Cancelled, entry.Outcome);
        Assert.True(r.Data.HasValue, "S37d/S90 data must identify the cancelled utterance that entered history.");
        Assert.Equal(entry.UtteranceId, DataString(r, "utterance_id"));
        Assert.Equal(SpeechStatuses.Cancelled, DataString(r, "outcome"));
    }
}

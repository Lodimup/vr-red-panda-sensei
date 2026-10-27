using Sensei.Engine.Speech;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.Speech;

/// <summary>S123 (both sides) and the presented-count rule of S89.</summary>
public sealed class EmptyContentTests
{
    [Fact]
    public async Task S123_a_card_with_only_empty_fields_is_invalid()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "explanation", Content(japanese: "", reading: "", romaji: "", english: ""));
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Empty(t.Events.Hints);
    }

    [Fact]
    public async Task S123_an_empty_field_is_absent_from_the_card()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "vocabulary_card", Content(japanese: "", english: "water"));
        Assert.Equal(ToolStatuses.Success, r.Status);
        var card = Assert.Single(t.Events.Hints);
        Assert.Null(card.Japanese);
        Assert.Equal("water", card.English);
    }

    [Fact]
    public async Task S123_an_empty_field_is_not_spoken()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "explanation", Content(japanese: "", english: "water"), speak: true);
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Equal(new[] { "en" }, t.Speech.Requests.Single().Segments.Select(s => s.Lang));
    }

    [Fact]
    public async Task S123_speaking_a_card_whose_speakable_fields_are_empty_says_nothing()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "explanation", Content(japanese: "", english: "", romaji: "mizu"), speak: true);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Empty(t.Speech.Requests);
    }

    [Theory]
    [InlineData(SpeechStatuses.Completed, null, 3, 3)]
    [InlineData(SpeechStatuses.Completed, 0, 3, 0)]
    [InlineData(SpeechStatuses.Completed, 2, 3, 2)]
    [InlineData(SpeechStatuses.Cancelled, null, 3, 0)]
    [InlineData(SpeechStatuses.Failed, 1, 3, 1)]
    [InlineData(SpeechStatuses.Failed, 9, 3, 3)]
    [InlineData(SpeechStatuses.Failed, -4, 3, 0)]
    public void S89_the_explicit_count_wins_and_a_completed_outcome_without_one_means_every_segment(string status, int? presented, int segments, int expected) =>
        Assert.Equal(expected, new SpeechOutcome(status, null, presented).PresentedOf(segments));
}

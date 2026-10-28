using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.Speech;

public sealed class ReviewS3HintEmptyFieldTests
{
    [Theory]
    [InlineData("japanese")]
    [InlineData("reading")]
    [InlineData("romaji")]
    public async Task S38a_an_empty_optional_field_does_not_invalidate_an_otherwise_nonempty_card(string field)
    {
        await using var t = await NewAsync();
        var content = Content(english: "water");
        content[field] = "";
        var r = await Hint(t, "vocabulary_card", content);
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Equal("water", Assert.Single(t.Events.Hints).English);
    }
}

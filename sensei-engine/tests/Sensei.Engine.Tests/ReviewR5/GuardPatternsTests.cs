using Sensei.Engine.Learner;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.ReviewR5;

public class GuardPatternsTests
{
    [Theory]
    [InlineData("WORKING  LEVEL")]
    [InlineData("working_level")]
    [InlineData("pre_n3")]
    [InlineData("your level is improving")]
    [InlineData("your JLPT level")]
    [InlineData("your Japanese level")]
    [InlineData("you're at the JLPT level pre-n one")]
    [InlineData("you are N2")]
    [InlineData("ur N three")]
    [InlineData("u r at N-four")]
    [InlineData("your jlpt japanese ability = jlpt pre-n five")]
    [InlineData("your level is N1")]
    [InlineData("あなたのレベル")]
    [InlineData("あなたは ＪＬＰＴ Ｎ５")]
    [InlineData("きみの日本語はN4")]
    [InlineData("君はn3")]
    public void ExplicitPatternsAreRefused(string text) => Assert.True(HiddenLevelGuard.Check(text));

    [Fact]
    public async Task SplitHintContentIsRefused()
    {
        await using var t = await NewAsync();
        var result = await Hint(t, "explanation", Content(japanese: "あなたは", english: "N4"));
        Assert.Equal("hidden_level", result.Detail);
        Assert.Empty(t.Events.Hints);
    }

    [Theory]
    [InlineData("きみのアクセント")]
    [InlineData("君のイントネーション")]
    [InlineData("学習者のピッチ")]
    [InlineData("あなたの発音")]
    public async Task JapaneseVocabularyLearnerClaimsAreRefused(string japanese)
    {
        await using var t = await NewAsync();
        var result = await Hint(t, "vocabulary_card", Content(japanese: japanese, english: "word"));
        Assert.Equal("pronunciation_claim", result.Detail);
        Assert.Empty(t.Events.Hints);
    }
}

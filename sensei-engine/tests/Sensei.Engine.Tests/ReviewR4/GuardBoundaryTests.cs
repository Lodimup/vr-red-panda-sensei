using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.ReviewR4;

public class GuardBoundaryTests
{
    [Theory]
    [InlineData("This is N5 vocabulary.", false)]
    [InlineData("JLPT N4 words are next.", false)]
    [InlineData("you can learn JLPT words", false)]
    [InlineData("Your Japanese level is improving", true)] // Orchestrator decision 2026-10-28 (not a Handler ruling): preserve existing learner-level phrases.
    [InlineData("Your level is improving", true)] // Orchestrator decision 2026-10-28 (not a Handler ruling): preserve existing learner-level phrases.
    [InlineData("U are Ｎ５", false)]
    [InlineData("U are Ｎ5", false)]
    [InlineData("きみのレベル", false)]
    [InlineData("pre_n3", true)]
    [InlineData("your banana5", false)]
    public void LevelRule(string text, bool refused) => Assert.Equal(refused, HiddenLevelGuard.Check(text));

    [Theory]
    [InlineData("accent", true)]
    [InlineData("The learner's pitch was wrong.", false)]
    [InlineData("your pronunciation was wrong", false)]
    public async Task VocabularyEnglish(string english, bool allowed)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("hello");
        var args = System.Text.Json.JsonSerializer.Serialize(new { kind = "vocabulary_card", content = new { japanese = "アクセント", english } });
        var result = await t.CallAsync("show_hint", args);
        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed) Assert.Equal("pronunciation_claim", result.Detail);
    }
}

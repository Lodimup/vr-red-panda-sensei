using Sensei.Engine.Learner;
using Xunit;

namespace Sensei.Engine.Tests.HiddenLevel;

/// <summary>S80 (round 3): text that tells the learner their own level is refused; talk about word levels stays allowed.</summary>
public class LearnerLevelClaimTests
{
    [Theory]
    [InlineData("Your JLPT level is pre_n5.")]
    [InlineData("your japanese level is improving")]
    [InlineData("Your level is N4 now.")]
    [InlineData("You're N5!")]
    [InlineData("You are at JLPT N3.")]
    [InlineData("you are an N2")]
    [InlineData("Your N5 is solid.")]
    [InlineData("working_level")]
    [InlineData("pre_n5")]
    [InlineData("あなたのレベルはN5です")]
    [InlineData("あなたはＮ5です")]
    public void Learner_level_claims_are_refused(string text) => Assert.True(HiddenLevelGuard.Check(text));

    [Theory]
    [InlineData("This is N5 vocabulary.")]
    [InlineData("JLPT N4 words are next.")]
    [InlineData("Your level of focus is great.")]
    [InlineData("working hard on your level")]
    [InlineData("This is a beginner word.")]
    [InlineData("pre-n3 grammar points")]
    public void General_level_talk_is_allowed(string text) => Assert.False(HiddenLevelGuard.Check(text));
}

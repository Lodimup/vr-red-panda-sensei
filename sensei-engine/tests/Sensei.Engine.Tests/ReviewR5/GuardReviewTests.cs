using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.ReviewR5;

public class GuardReviewTests
{
    [Theory]
    [InlineData("Hello! Let's practice Japanese.")]
    [InlineData("Can you point to the apple?")]
    [InlineData("Please move the cup to the left.")]
    [InlineData("Let's say the word together.")]
    [InlineData("Great! You found the red apple.")]
    [InlineData("Would you like a hint?")]
    [InlineData("The word for water is mizu.")]
    [InlineData("Listen first, then repeat after me.")]
    [InlineData("Try saying it slowly.")]
    [InlineData("What color is the book?")]
    [InlineData("That's right; the cup is blue.")]
    [InlineData("It's okay. Let's try again.")]
    [InlineData("This is N5 vocabulary.")]
    [InlineData("The JLPT has five levels.")]
    [InlineData("Let's learn a new word.")]
    [InlineData("Can you say the word for cat?")]
    [InlineData("You can practice N5 words with me.")]
    [InlineData("Would you like to learn JLPT vocabulary?")]
    [InlineData("Can you find the N5 word for water?")]
    public async Task OrdinaryTeachingSentenceIsAllowed(string text)
    {
        await using var t = await NewAsync();
        var result = await SpeakOne(t, "en", text);
        Assert.True(result.IsSuccess, $"{text}: {result.Status}/{result.Detail}");
    }

    [Theory]
    [InlineData("You are Ｎ５.")]
    [InlineData("Your Japanese ability is N-5.")]
    [InlineData("You are at the N five level.")]
    [InlineData("Ｙｏｕ are at N5.")]
    public async Task LearnerLevelIsNotSpoken(string text)
    {
        await using var t = await NewAsync();
        t.Core.Learner.Profile.WorkingLevel = "n5";
        var result = await SpeakOne(t, "en", text);
        Assert.DoesNotContain(t.Speech.Requests.SelectMany(r => r.Segments), s => s.Text == text);
    }

    [Fact]
    public async Task LevelClaimSplitAcrossSpeechSegmentsIsNotSpoken()
    {
        await using var t = await NewAsync();
        t.Core.Learner.Profile.WorkingLevel = "n4";
        var result = await Speak(t, Segment("en", "You are at"), Segment("en", "N4."));
        Assert.Empty(t.Speech.Requests);
    }

    [Fact]
    public async Task BilingualLevelClaimSplitAcrossSpeechSegmentsIsNotSpoken()
    {
        await using var t = await NewAsync();
        t.Core.Learner.Profile.WorkingLevel = "n4";
        var result = await Speak(t, Segment("ja", "あなたは"), Segment("en", "N4."));
        Assert.Empty(t.Speech.Requests);
    }

    [Fact]
    public async Task JapaneseVocabularyCardCannotStatePronunciationVerdict()
    {
        await using var t = await NewAsync();
        var result = await Hint(t, "vocabulary_card", Content(japanese: "あなたの発音は間違っています。", english: "pronunciation"), speak: true);
        Assert.Empty(t.Events.Hints);
    }
}

using System.Linq;
using NUnit.Framework;
using Sensei.App.Core;
using Sensei.Engine.Speech;

public sealed class SpeechChunkerTests
{
    [TestCase("Hi! Longer explanation. Next?", new[] { "Hi! ", "Longer explanation. ", "Next?" })]
    [TestCase("はい。 りんごです！ 次？", new[] { "はい。 ", "りんごです！ ", "次？" })]
    [TestCase("3.14 is pi. Yes!", new[] { "3.14 is pi. ", "Yes!" })]
    [TestCase("はい。続き", new[] { "はい。続き" })]
    [TestCase("One!\nTwo", new[] { "One!\n", "Two" })]
    public void SplitsOnlyAtSpecifiedBoundariesAndPreservesText(string input, string[] expected)
    {
        var segment = new SpeechSegment("en", input, null, null, null);
        var chunks = SpeechChunker.Split(new[] { segment });
        CollectionAssert.AreEqual(expected, chunks.Select(c => c.Text));
        Assert.That(string.Concat(chunks.Select(c => c.Text)), Is.EqualTo(input));
        Assert.That(chunks[0].First, Is.True);
        Assert.That(chunks.Skip(1).All(c => !c.First && c.Segment == segment), Is.True);
    }

    [Test]
    public void FirstChunkOfEachSegmentRetainsOriginalCaptionAndIndex()
    {
        var a = new SpeechSegment("en", "Hello! More.", null, null, "translation");
        var b = new SpeechSegment("ja", "はい。 次。", "reading", "romaji", "yes");
        var chunks = SpeechChunker.Split(new[] { a, b });
        CollectionAssert.AreEqual(new[] { 0, 0, 1, 1 }, chunks.Select(c => c.SegmentIndex));
        CollectionAssert.AreEqual(new[] { true, false, true, false }, chunks.Select(c => c.First));
        Assert.That(chunks[0].Segment, Is.SameAs(a));
        Assert.That(chunks[2].Segment, Is.SameAs(b));
    }
}

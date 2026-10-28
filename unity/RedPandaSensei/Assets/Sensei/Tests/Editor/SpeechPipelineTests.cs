using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sensei.App.Core;

public class SpeechPipelineTests
{
    [Test]
    public async Task SynthesisOfNextStartsDuringPlaybackAndSegmentsStayOrdered()
    {
        var trace = new List<string>(); var first = new TaskCompletionSource<PlayOutcome>();
        var run = SpeechPipeline.PlayAsync(new[] { "one", "two" }, s => { trace.Add("synth " + s); return Task.FromResult(s); },
            (s, audio) => { trace.Add("play " + s); return s == "one" ? first.Task : Task.FromResult(PlayOutcome.Completed); }, () => true, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "synth one", "play one", "synth two" }, trace);
        first.SetResult(PlayOutcome.Completed); Assert.AreEqual(2, await run);
        CollectionAssert.AreEqual(new[] { "synth one", "play one", "synth two", "play two" }, trace);
    }
    [Test]
    public async Task StaleGenerationNeverPlaysTheNextSynthesizedSegment()
    {
        bool current = true; var first = new TaskCompletionSource<PlayOutcome>(); var played = new List<string>();
        var run = SpeechPipeline.PlayAsync(new[] { "one", "two" }, s => Task.FromResult(s),
            (s, audio) => { played.Add(s); return first.Task; }, () => current, CancellationToken.None);
        current = false; first.SetResult(PlayOutcome.Completed); Assert.AreEqual(1, await run); CollectionAssert.AreEqual(new[] { "one" }, played);
    }
    [TestCase(PlayOutcome.StoppedAfterStart, 1)]
    [TestCase(PlayOutcome.NotStarted, 0)]
    public async Task InterruptedPlaybackCountsOnlyStartedSegments(PlayOutcome outcome, int expected)
    {
        int reported = -1;
        var result = await SpeechPipeline.PlayAsync(new[] { "one", "two" }, s => Task.FromResult(s),
            (s, audio) => Task.FromResult(outcome), () => true, CancellationToken.None, n => reported = n);
        Assert.AreEqual(expected, result);
        Assert.AreEqual(expected == 0 ? -1 : expected, reported);
    }
    [TestCase("API_KEY=secret1234")]
    [TestCase("SK-abcdefghijklmnop")]
    [TestCase("abcdefghijklmnopqrstuvwxyz0123456789")]
    public void CredentialPatternsAreRedacted(string input) => Assert.AreEqual("[redacted]", LogRedactor.Redact(input, null));
    [TestCase("password")]
    [TestCase("API key")]
    [TestCase("antidisestablishmentarianism")]
    [TestCase("今日はpasswordという単語を練習したい")]
    public void OrdinaryWordsSurviveRedaction(string input) => Assert.AreEqual(input, LogRedactor.Redact(input, null));
    [Test]
    public void ConfiguredKeyIsRedacted() => Assert.AreEqual("value [redacted]", LogRedactor.Redact("value exact-key", "exact-key"));
    [Test]
    public void CacheKeysIncludeTextLanguageAndSpeedAndEvictsLeastRecentlyUsed()
    {
        var cache = new SpeechCache<int>(2); cache.Put("a", "ja", 1, 1); cache.Put("a", "en", 1, 2);
        Assert.IsTrue(cache.TryGet("a", "ja", 1, out _)); cache.Put("a", "ja", .8f, 3);
        Assert.IsFalse(cache.TryGet("a", "en", 1, out _)); Assert.IsTrue(cache.TryGet("a", "ja", 1, out var normal));
        Assert.IsTrue(cache.TryGet("a", "ja", .8f, out var slow)); Assert.AreEqual(1, normal); Assert.AreEqual(3, slow);
        Assert.IsFalse(cache.TryGet("b", "ja", 1, out _));
    }
}

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sensei.App;
using Sensei.App.Core;
using Sensei.Engine.Speech;
using Sensei.Unity;
using Sensei.Voice;
using UnityEngine;

public class ReviewU2Tests
{
    [TestCase("API_KEY=secret1234")]
    [TestCase("SK-abcdefghijklmnop")]
    [TestCase("abcdefghijklmnopqrstuvwxyz0123456789")]
    public void CredentialPatternMustBeRedactedInDebugTranscript(string secret)
    {
        Assert.That(LogSpeechOutput.Redact(secret, null), Is.EqualTo("[redacted]"));
    }

    [Test]
    public void CaptionMustWaitForPlayerToStartQueuedAudio()
    {
        var go = new GameObject("review-player");
        try
        {
            var player = go.AddComponent<SpeechPlayer>();
            typeof(SpeechPlayer).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
            var queue = new MainThreadQueue();
            var output = new LocalSpeechOutput(null, player, queue);
            var cache = (SpeechCache<TtsAudio>)typeof(LocalSpeechOutput).GetField("cache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);
            cache.Put("hello", "en", 1f, new TtsAudio(new float[16000], 16000));
            bool caption = false; output.SegmentStarted += _ => caption = true;
            output.SpeakAsync(new SpeechRequest("u1", 1, new[] { new SpeechSegment("en", "hello", null, null, null) }, "normal"), CancellationToken.None);
            queue.Drain(); // engine queue runs before SpeechPlayer.Update, a valid Unity Update ordering
            Assert.That(caption, Is.False, "caption appeared before queued audio began playback");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}

public class ChunkPresentationTests
{
    [Test]
    public async Task InterruptedFirstChunkCountsOneWholeSegmentAndShowsItsCaptionOnce()
    {
        var go = new GameObject("started-chunk-player");
        try
        {
            var player = go.AddComponent<SpeechPlayer>();
            typeof(SpeechPlayer).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
            var queue = new MainThreadQueue(); var output = new LocalSpeechOutput(null, player, queue);
            var cache = (SpeechCache<TtsAudio>)typeof(LocalSpeechOutput).GetField("cache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);
            cache.Put("Hi! ", "en", 1f, new TtsAudio(new float[16000], 16000));
            cache.Put("More.", "en", 1f, new TtsAudio(new float[16000], 16000));
            var original = new SpeechSegment("en", "Hi! More.", null, null, null);
            int captions = 0; SpeechSegment caption = null;
            output.SegmentStarted += segment => { captions++; caption = segment; };
            var task = output.SpeakAsync(new SpeechRequest("u", 1, new[] { original }, "normal"), CancellationToken.None);
            typeof(SpeechPlayer).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
            queue.Drain();
            var clip = (AudioClip)typeof(SpeechPlayer).GetField("clip", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Destroy may not be called from edit mode"));
            output.Interrupt();
            UnityEngine.Object.DestroyImmediate(clip);
            var result = await task;
            Assert.That(result.SegmentsPresented, Is.EqualTo(1));
            Assert.That(captions, Is.EqualTo(1));
            Assert.That(caption, Is.SameAs(original));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public async Task CancellationBeforeFirstChunkStartsDoesNotCountOrShowCaption()
    {
        var go = new GameObject("chunk-player");
        try
        {
            var player = go.AddComponent<SpeechPlayer>();
            typeof(SpeechPlayer).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
            var queue = new MainThreadQueue(); var output = new LocalSpeechOutput(null, player, queue);
            var cache = (SpeechCache<TtsAudio>)typeof(LocalSpeechOutput).GetField("cache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);
            cache.Put("Hi! ", "en", 1f, new TtsAudio(new float[16000], 16000));
            cache.Put("More.", "en", 1f, new TtsAudio(new float[16000], 16000));
            int captions = 0; output.SegmentStarted += _ => captions++;
            var task = output.SpeakAsync(new SpeechRequest("u", 1, new[] { new SpeechSegment("en", "Hi! More.", null, null, null) }, "normal"), CancellationToken.None);
            output.Interrupt();
            typeof(SpeechPlayer).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
            var result = await task; queue.Drain();
            Assert.That(result.SegmentsPresented, Is.EqualTo(0));
            Assert.That(captions, Is.EqualTo(0));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}

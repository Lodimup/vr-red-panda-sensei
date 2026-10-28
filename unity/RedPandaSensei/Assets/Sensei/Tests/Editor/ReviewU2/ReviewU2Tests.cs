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

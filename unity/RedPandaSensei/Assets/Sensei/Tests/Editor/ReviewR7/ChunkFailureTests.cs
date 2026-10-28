using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sensei.App;
using Sensei.App.Core;
using Sensei.Engine.Speech;
using Sensei.Voice;
using UnityEngine;
using UnityEngine.TestTools;

public class ChunkFailureTests
{
    [Test] public async Task FailureInSecondChunkReportsItsOwnSegmentLanguage()
    {
        var go = new GameObject("chunk-failure");
        try
        {
            var player = go.AddComponent<SpeechPlayer>();
            typeof(SpeechPlayer).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
            var output = new LocalSpeechOutput(null, player, new MainThreadQueue());
            var cache = (SpeechCache<TtsAudio>)typeof(LocalSpeechOutput).GetField("cache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);
            cache.Put("はい。 ", "ja", 1f, new TtsAudio(new float[16000], 16000));
            string logged = null;
            Application.LogCallback capture = (condition, stack, type) => { if (condition.StartsWith("RPS_SPEAK failed")) logged = condition; };
            Application.logMessageReceived += capture;
            try
            {
                var segments = new[] { new SpeechSegment("ja", "はい。 次。", null, null, null), new SpeechSegment("en", "Hello", null, null, null) };
                var task = output.SpeakAsync(new SpeechRequest("u", 1, segments, "normal"), CancellationToken.None);
                typeof(SpeechPlayer).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
                var clip = (AudioClip)typeof(SpeechPlayer).GetField("clip", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
                LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));
                LogAssert.Expect(LogType.Error, new Regex("RPS_SPEAK failed lang=(ja|en) chars=(6|5)"));
                typeof(SpeechPlayer).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, new object[] { true });
                UnityEngine.Object.DestroyImmediate(clip);
                var result = await task;
                Assert.That(result.Status, Is.EqualTo(SpeechStatuses.Failed));
                Assert.That(logged, Is.EqualTo("RPS_SPEAK failed lang=ja chars=6"), "failure happened in the second chunk of the Japanese segment");
            }
            finally { Application.logMessageReceived -= capture; }
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}

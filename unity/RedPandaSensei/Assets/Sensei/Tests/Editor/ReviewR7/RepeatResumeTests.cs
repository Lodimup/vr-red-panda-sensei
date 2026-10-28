using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sensei.App;
using Sensei.Engine;
using Sensei.Engine.Storage;
using Sensei.Engine.Speech;
using UnityEngine;
using Object = UnityEngine.Object;

public class RepeatResumeTests
{
    sealed class Speech : ISpeechOutput
    {
        public int Calls;
        public Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return Task.FromResult(new SpeechOutcome("completed", null)); }
    }
    static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, value);
    static void Pause(EngineHost host, bool paused) => typeof(EngineHost).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, new object[] { paused });

    [Test] public async Task DuplicateResumeMustNotDetachQueuedRepeatFromNextPause()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rps-r7-" + Guid.NewGuid());
        var speech = new Speech();
        var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient = null, Speech = speech, ProfileStore = new FileProfileStore(dir) });
        engine.BeginTurn("test");
        Assert.That((await engine.InvokeToolAsync("speak", "{\"segments\":[{\"lang\":\"en\",\"text\":\"water\"}]} ")).IsSuccess, Is.True);
        var go = new GameObject("host"); var host = go.AddComponent<EngineHost>();
        Set(host, "engine", engine); Set(host, "ready", true);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        Set(host, "repeatBeforeEngine", (Action)(() => { entered.TrySetResult(true); release.Wait(TimeSpan.FromSeconds(10)); }));
        try
        {
            var repeat = host.Repeat();
            Assert.That(await Task.WhenAny(entered.Task, Task.Delay(10000)), Is.SameAs(entered.Task));
            Pause(host, false); // duplicate resume callback while the worker is in the epoch-to-engine gap
            Pause(host, true);
            release.Set();
            await repeat;
            Assert.That(speech.Calls, Is.EqualTo(1), "pause must prevent a stale replay from reaching speech");
        }
        finally { release.Set(); Set(host, "engine", null); Object.DestroyImmediate(go); await engine.DisposeAsync(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

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
using Sensei.Engine.Tools;
using UnityEngine;
using Object = UnityEngine.Object;

public class RepeatEpochTests
{
    sealed class Speech : ISpeechOutput { public int Calls; public Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult(new SpeechOutcome("completed", null)); } }
    static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, value);

    [Test] public async Task QueuedRepeatIsDroppedAfterPauseAndNewRepeatStillRuns()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rps-repeat-" + Guid.NewGuid());
        var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient = null, Speech = new Speech(), ProfileStore = new FileProfileStore(dir) });
        var go = new GameObject("host"); var host = go.AddComponent<EngineHost>();
        Set(host, "engine", engine); Set(host, "ready", true);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Func<Task<ToolResult>>, Task<ToolResult>> scheduler = async work => { await release.Task; return await work(); };
        Set(host, "repeatScheduler", scheduler);
        try
        {
            var repeat = host.Repeat();
            typeof(EngineHost).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, new object[] { true });
            release.SetResult(true);
            Assert.That(await repeat, Is.False);
            typeof(EngineHost).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, new object[] { false });
            // With no speech history, an engine call reports nothing_to_replay, and Repeat returns true.
            Assert.That(await host.Repeat(), Is.True);
        }
        finally { Set(host, "engine", null); Object.DestroyImmediate(go); await engine.DisposeAsync(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Test] public async Task PauseAfterEpochCheckCancelsRepeatBeforeEngineEntry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rps-repeat-late-" + Guid.NewGuid());
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
            typeof(EngineHost).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, new object[] { true });
            release.Set();
            await repeat;
            Assert.That(speech.Calls, Is.EqualTo(1));
        }
        finally { release.Set(); Set(host, "engine", null); Object.DestroyImmediate(go); await engine.DisposeAsync(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

}

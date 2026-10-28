using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using NUnit.Framework;
using Sensei.App;
using Sensei.App.Core;
using Sensei.Engine;
using Sensei.Engine.Speech;
using Sensei.Engine.Storage;
using Sensei.Voice;
using UnityEngine;
using Object = UnityEngine.Object;

public class PauseTests
{
    sealed class Speech : ISpeechOutput { public Task<SpeechOutcome> SpeakAsync(SpeechRequest r, CancellationToken ct) => Task.FromResult(new SpeechOutcome("completed", null)); }
    sealed class BlockingChat : IChatClient
    {
        public readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions o = null, CancellationToken ct = default) { Entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, ct); throw new Exception("unreachable"); }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions o = null, CancellationToken ct = default) { var r = await GetResponseAsync(m, o, ct); foreach (var u in r.ToChatResponseUpdates()) yield return u; }
        public object GetService(Type t, object key = null) => null;
        public void Dispose() { }
    }
    static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, value);
    static void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args);

    [Test] public async Task PauseCancelsGenerationAndStopsSpeechWithoutReplayingOnResume()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rps-pause-" + Guid.NewGuid());
        var client = new BlockingChat();
        var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient = client, Speech = new Speech(), ProfileStore = new FileProfileStore(dir) });
        var hostGo = new GameObject("host"); var menuGo = new GameObject("menu");
        var host = hostGo.AddComponent<EngineHost>(); var player = hostGo.AddComponent<SpeechPlayer>();
        var speech = new LocalSpeechOutput(null, player, new MainThreadQueue());
        Set(host, "engine", engine); Set(host, "ready", true); Set(host, "hasKey", true); Set(host, "menu", menuGo.AddComponent<MenuPanel>()); Set(host, "player", player); Set(host, "speech", speech);
        try
        {
            speech.Interrupt();
            var playback = player.PlayAsync(new TtsAudio(new float[16000], 16000), 0, CancellationToken.None, null);
            var turn = host.EventTurn("help_hint"); await client.Entered.Task;
            Call(host, "OnApplicationPause", true);
            Call(player, "Update");
            Assert.That(await playback, Is.EqualTo(PlayOutcome.NotStarted));
            Assert.That(await turn, Is.False);
            Call(host, "OnApplicationPause", false);
            Call(player, "Update");
            Assert.That(host.SpeechPlaying, Is.False);
            Assert.That(host.VoiceIdle, Is.True);
        }
        finally { engine.CancelGeneration(); Set(host, "engine", null); Object.DestroyImmediate(hostGo); Object.DestroyImmediate(menuGo); await engine.DisposeAsync(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

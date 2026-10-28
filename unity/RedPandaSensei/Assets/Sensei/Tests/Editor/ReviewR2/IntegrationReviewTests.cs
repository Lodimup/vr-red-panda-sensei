using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using NUnit.Framework;
using Sensei.App;
using Sensei.Engine;
using Sensei.Engine.Speech;
using Sensei.Engine.Storage;
using Sensei.Voice;
using UnityEngine;
using Object = UnityEngine.Object;

public class IntegrationReviewTests
{
    sealed class Speech : ISpeechOutput { public Task<SpeechOutcome> SpeakAsync(SpeechRequest r, CancellationToken ct) => Task.FromResult(new SpeechOutcome("completed", null)); }
    sealed class BlockingChat : IChatClient
    {
        int calls;
        public TaskCompletionSource<bool> First = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions o = null, CancellationToken ct = default)
        {
            int n = Interlocked.Increment(ref calls); (n == 1 ? First : Second).TrySetResult(true);
            await Task.Delay(Timeout.Infinite, ct); throw new Exception("unreachable");
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions o = null, CancellationToken ct = default)
        { var response = await GetResponseAsync(m, o, ct); foreach (var update in response.ToChatResponseUpdates()) yield return update; }
        public object GetService(Type t, object key = null) => null;
        public void Dispose() {}
    }
    static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o,value);
    [Test] public async Task SupersededTurnMustNotMarkItsSuccessorIdle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rps-r2-" + Guid.NewGuid()); var client = new BlockingChat();
        var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient=client, Speech=new Speech(), ProfileStore=new FileProfileStore(dir) });
        var hostGo=new GameObject("host"); var menuGo=new GameObject("menu"); var host=hostGo.AddComponent<EngineHost>();
        Set(host,"engine",engine); Set(host,"ready",true); Set(host,"hasKey",true); Set(host,"menu",menuGo.AddComponent<MenuPanel>());
        try {
            var first=host.EventTurn("help_hint"); await client.First.Task;
            var second=host.EventTurn("help_repeat"); await client.Second.Task; await first;
            Assert.That(second.IsCompleted, Is.False, "second request is still blocked in the provider");
            Assert.That(host.VoiceIdle, Is.False, "a cancelled predecessor must not mark its running successor idle");
        } finally { engine.CancelGeneration(); Set(host,"engine",null); Object.DestroyImmediate(hostGo); Object.DestroyImmediate(menuGo); await engine.DisposeAsync(); if (Directory.Exists(dir)) Directory.Delete(dir,true); }
    }
    [Test] public async Task InterruptedStagingIsReplacedOnNextInstall()
    {
        string name="rps-review-"+Guid.NewGuid().ToString("N"); string source=Path.Combine(Application.streamingAssetsPath,name); string target=Path.Combine(Application.persistentDataPath,name); string staging=target+".installing";
        try {
            Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source,"model.bin"),"complete");
            Directory.CreateDirectory(staging); File.WriteAllText(Path.Combine(staging,"model.bin"),"partial");
            var result=await VoiceModelInstaller.EnsureInstalledAsync(name,"v1",CancellationToken.None);
            Assert.AreEqual("complete",File.ReadAllText(Path.Combine(result,"model.bin"))); Assert.IsTrue(File.Exists(Path.Combine(result,".installed-v1"))); Assert.IsFalse(Directory.Exists(staging));
        } finally { if(Directory.Exists(source)) Directory.Delete(source,true); if(Directory.Exists(target)) Directory.Delete(target,true); if(Directory.Exists(staging)) Directory.Delete(staging,true); }
    }
}

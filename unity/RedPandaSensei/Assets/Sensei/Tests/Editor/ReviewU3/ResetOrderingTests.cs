using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sensei.App;
using Sensei.App.Core;
using Sensei.Engine;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Storage;
using Sensei.Engine.Tasks;
using Sensei.Engine.World;
using UnityEngine;
using Object = UnityEngine.Object;

public class ResetOrderingTests
{
    class Speech : ISpeechOutput { public Task<SpeechOutcome> SpeakAsync(SpeechRequest r,CancellationToken ct) => Task.FromResult(new SpeechOutcome("completed",null)); }
    class Events : IEngineEvents {
        public Action<TaskView> OnTask;
        public void OnTaskChanged(TaskView t) { OnTask?.Invoke(t); }
        public void OnHint(Sensei.Engine.Events.HintCard c) {} public void OnSceneChanged(SceneEvent e,SceneSnapshot s) {} public void OnHighlight(IReadOnlyList<string> i,int d) {} public void OnAnimate(string a,string id) {} public void OnSupportChanged(SupportSettings s) {} public void OnSummary(SessionSummary s) {} public void OnSenseiState(string s) {}
    }
    [Test] public async Task ResetLeavesNoTaskCardAfterQueuedEngineNotifications()
    {
        var dir=Path.Combine(Path.GetTempPath(),"rps-reset-"+Guid.NewGuid()); var events=new Events();
        var engine=await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient=null,Speech=new Speech(),ProfileStore=new FileProfileStore(dir),Events=events });
        var area=new GameObject("area"); var taskGo=new GameObject("task"); var hintGo=new GameObject("hint"); var hostGo=new GameObject("host");
        try {
            var task=taskGo.AddComponent<TaskCard>(); task.Initialize(area.transform,null);
            var hint=hintGo.AddComponent<Sensei.App.HintCard>(); hint.Initialize(area.transform,null);
            var host=hostGo.AddComponent<EngineHost>(); var f=BindingFlags.NonPublic|BindingFlags.Instance;
            typeof(EngineHost).GetField("engine",f).SetValue(host,engine); typeof(EngineHost).GetField("taskCard",f).SetValue(host,task); typeof(EngineHost).GetField("hintCard",f).SetValue(host,hint);
            var queue=(MainThreadQueue)typeof(EngineHost).GetField("queue",f).GetValue(host);
            events.OnTask=t => queue.Post(() => { hint.Clear(); task.Show(t); });
            engine.BeginTurn("test"); Assert.That((await engine.InvokeToolAsync("spawn_objects","{\"objects\":[{\"catalog_id\":\"apple\",\"region\":\"left\"}]} ")).Status,Is.EqualTo("success"));
            engine.BeginTurn("next"); Assert.That((await engine.InvokeToolAsync("start_challenge","{\"family\":\"listen_arrange\",\"instruction\":{\"english\":\"Move apple\"},\"skill_targets\":[{\"dimension\":\"listening\",\"key\":\"apple\"}],\"predicates\":[{\"type\":\"in_region\",\"object_id\":\"apple_1\",\"region\":\"right\"}]}")).Status,Is.EqualTo("success"));
            queue.Drain(); Assert.That(taskGo.activeSelf,Is.True);
            Assert.That(await host.ResetProgress(),Is.True); Assert.That(engine.GetActiveTask(),Is.Null); Assert.That(taskGo.activeSelf,Is.False);
            queue.Drain();
            Assert.That(taskGo.activeSelf,Is.False,"reset's queued abandoned-task event must not resurrect the cleared card");
        } finally { typeof(EngineHost).GetField("engine",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(hostGo.GetComponent<EngineHost>(),null); Object.DestroyImmediate(hostGo); Object.DestroyImmediate(taskGo); Object.DestroyImmediate(hintGo); Object.DestroyImmediate(area); await engine.DisposeAsync(); if(Directory.Exists(dir)) Directory.Delete(dir,true); }
    }
}

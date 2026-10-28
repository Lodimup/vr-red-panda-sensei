using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sensei.Engine;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Storage;
using Sensei.Engine.Tasks;
using Sensei.Engine.World;
using Sensei.App;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public class EngineReachabilityTests
{
    class Speech : ISpeechOutput { public Task<SpeechOutcome> SpeakAsync(SpeechRequest r, CancellationToken ct) => Task.FromResult(new SpeechOutcome("completed", null)); }
    class Events : IEngineEvents {
        public Sensei.Engine.Events.HintCard Hint; public TaskView Task;
        public void OnHint(Sensei.Engine.Events.HintCard c) { Hint=c; }
        public void OnTaskChanged(TaskView t) { Task=t; }
        public void OnSceneChanged(SceneEvent e, SceneSnapshot s) {} public void OnHighlight(IReadOnlyList<string> i,int d) {} public void OnAnimate(string a,string id) {}
        public void OnSupportChanged(SupportSettings s) {} public void OnSummary(SessionSummary s) {} public void OnSenseiState(string s) {}
    }
    [Test] public async Task ActualEngineHintDoesNotShowCredential()
    {
        const string secret="sk-abcdefghijklmnopqrstuvwx";
        var dir=Path.Combine(Path.GetTempPath(), "rps-review-" + Guid.NewGuid()); var events=new Events();
        var engine=await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient=null, Speech=new Speech(), ProfileStore=new FileProfileStore(dir), Events=events });
        var area=new GameObject("area"); var go=new GameObject("hint");
        try {
            engine.BeginTurn("test");
            var result=await engine.InvokeToolAsync("show_hint", "{\"kind\":\"explanation\",\"content\":{\"english\":\""+secret+"\"}}");
            Assert.That(result.Status, Is.EqualTo("success"), "engine must accept test input before UI assertion"); Assert.That(events.Hint, Is.Not.Null);
            var card=go.AddComponent<Sensei.App.HintCard>(); card.Initialize(area.transform,null); card.Show(events.Hint);
            StringAssert.DoesNotContain(secret, go.GetComponentInChildren<TextMeshPro>(true).text);
        } finally { Object.DestroyImmediate(go); Object.DestroyImmediate(area); await engine.DisposeAsync(); if(Directory.Exists(dir)) Directory.Delete(dir,true); }
    }
    [Test] public async Task ActualEngineRefusesTaskThatRevealsHiddenWorkingLevel()
    {
        var dir=Path.Combine(Path.GetTempPath(), "rps-review-" + Guid.NewGuid()); var events=new Events();
        var engine=await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient=null, Speech=new Speech(), ProfileStore=new FileProfileStore(dir), Events=events });
        var area=new GameObject("area"); var go=new GameObject("task");
        try {
            engine.BeginTurn("test");
            var spawn=await engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"apple\",\"region\":\"left\"}]}"); Assert.That(spawn.Status,Is.EqualTo("success"));
            engine.BeginTurn("test next round");
            var hiddenLevel=engine.GetProfile().WorkingLevel;
            var result=await engine.InvokeToolAsync("start_challenge", "{\"family\":\"listen_arrange\",\"instruction\":{\"english\":\"Your working level is " + hiddenLevel + ".\"},\"skill_targets\":[{\"dimension\":\"listening\",\"key\":\"apple\"}],\"predicates\":[{\"type\":\"in_region\",\"object_id\":\"apple_1\",\"region\":\"right\"}]}");
            Assert.That(result.Status, Is.Not.EqualTo("success"));
            Assert.That(result.Detail, Is.EqualTo("hidden_level"));
            Assert.That(events.Task, Is.Null);
        } finally { Object.DestroyImmediate(go); Object.DestroyImmediate(area); await engine.DisposeAsync(); if(Directory.Exists(dir)) Directory.Delete(dir,true); }
    }
}

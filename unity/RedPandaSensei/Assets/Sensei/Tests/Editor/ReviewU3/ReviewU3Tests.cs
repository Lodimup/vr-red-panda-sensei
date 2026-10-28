using System;
using System.Reflection;
using NUnit.Framework;
using Sensei.App;
using Sensei.App.Core;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tools.Speech;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ReviewU3Tests
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static string PanelText(GameObject panel) => panel.GetComponentInChildren<TextMeshPro>(true).text;

    [Test] public void FailedVoiceModelIsNamedOnStatusPanel()
    {
        var head = new GameObject("head"); var box = new GameObject("status"); var owner = new GameObject("owner");
        try {
            var status = box.AddComponent<StatusPanel>(); status.Initialize(head.transform, null);
            var host = owner.AddComponent<EngineHost>(); Set(host, "statusPanel", status);
            typeof(EngineHost).GetMethod("Report", Private).Invoke(host, new object[]{"RPS_BOOT step=3 fail Supertonic IOException"});
            StringAssert.Contains("Supertonic", PanelText(box), "U1/U2 require naming the model that failed");
        } finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(box); Object.DestroyImmediate(head); }
    }

    [Test] public void HintNeverDisplaysCredentialShapedContentAcceptedByEngine()
    {
        const string secret = "sk-abcdefghijklmnopqrstuvwx";
        var args = new ShowHintArgs { Kind = "explanation", Content = new ShowHintContent { English = secret } };
        Assert.That(new ShowHintTool().ValidateArguments(args), Is.Null, "engine accepts this hint content");
        var area = new GameObject("area"); var go = new GameObject("hint");
        try {
            var card = go.AddComponent<Sensei.App.HintCard>(); card.Initialize(area.transform, null);
            card.Show(new Sensei.Engine.Events.HintCard { Kind="explanation", English=secret });
            StringAssert.DoesNotContain(secret, PanelText(go), "credential-shaped text must not appear on a panel");
        } finally { Object.DestroyImmediate(go); Object.DestroyImmediate(area); }
    }

    [Test] public void HintStormPresentsLatestStateWithinOneFrame()
    {
        var queue = new MainThreadQueue(); var events = new UnityEngineEvents(queue, null, null, _ => {}); string seen = null;
        events.Hint = hint => seen = hint.English;
        for (var i = 0; i < 1000; i++) events.OnHint(new Sensei.Engine.Events.HintCard { Kind="explanation", English=i.ToString() });
        queue.Drain(); // EngineHost.Update's default limit
        Assert.That(seen, Is.EqualTo("999"), "obsolete events must not keep the current card behind a backlog");
    }
}

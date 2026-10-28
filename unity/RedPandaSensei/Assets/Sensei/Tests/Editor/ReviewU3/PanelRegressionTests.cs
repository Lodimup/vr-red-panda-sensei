using NUnit.Framework;
using Sensei.App;
using Sensei.App.Core;
using Sensei.Engine.Events;
using Sensei.Engine.Tasks;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public class PanelRegressionTests
{
    [Test] public void ResetDropsQueuedHintAndAcceptsNewHint()
    {
        var queue = new MainThreadQueue(); var events = new UnityEngineEvents(queue, null, null, _ => {}); string seen = null;
        events.Hint = h => seen = h.English;
        events.OnHint(new Sensei.Engine.Events.HintCard { English = "old" }); events.ResetPanels(); queue.Drain();
        Assert.That(seen, Is.Null);
        events.OnHint(new Sensei.Engine.Events.HintCard { English = "new" }); queue.Drain(); Assert.That(seen, Is.EqualTo("new"));
    }
    [Test] public void TaskStormKeepsOnlyLatest()
    {
        var queue = new MainThreadQueue(); var events = new UnityEngineEvents(queue, null, null, _ => {}); string seen = null;
        events.Task = t => seen = t.TaskId;
        for (int i = 0; i < 1000; i++) events.OnTaskChanged(new TaskView { TaskId = i.ToString() });
        Assert.That(queue.Drain(), Is.EqualTo(1)); Assert.That(seen, Is.EqualTo("999"));
    }
    [Test] public void TaskAndHintRedactExactConfiguredKey()
    {
        var area = new GameObject("area"); var taskGo = new GameObject("task"); var hintGo = new GameObject("hint");
        try {
            var task = taskGo.AddComponent<TaskCard>(); task.Initialize(area.transform, null); task.SetKey("private-test-value");
            task.SetSupport(new Sensei.Engine.Learner.SupportSettings { ShowTranslation = true });
            task.Show(new TaskView { Instruction = new TaskInstruction { English = "private-test-value" } });
            var hint = hintGo.AddComponent<Sensei.App.HintCard>(); hint.Initialize(area.transform, null); hint.SetKey("private-test-value"); hint.Show(new Sensei.Engine.Events.HintCard { English = "private-test-value" });
            StringAssert.DoesNotContain("private-test-value", taskGo.GetComponentInChildren<TextMeshPro>(true).text);
            StringAssert.DoesNotContain("private-test-value", hintGo.GetComponentInChildren<TextMeshPro>(true).text);
        } finally { Object.DestroyImmediate(taskGo); Object.DestroyImmediate(hintGo); Object.DestroyImmediate(area); }
    }
}

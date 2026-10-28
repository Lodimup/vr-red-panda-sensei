using NUnit.Framework;
using System.Reflection;
using Sensei.App;
using Sensei.App.Core;
using UnityEngine;

public sealed class PanelLayoutTests
{
    [Test] public void SlotsDoNotOverlapIncludingSensei()
    {
        var slots = new[] { PanelLayout.Sensei, PanelLayout.Caption, PanelLayout.YouSaid, PanelLayout.Task, PanelLayout.Hint, PanelLayout.Status };
        for (int i = 0; i < slots.Length; i++) for (int j = i + 1; j < slots.Length; j++)
            Assert.That(slots[i].Bounds.Overlaps(slots[j].Bounds), Is.False, $"slots {i} and {j}");
        Assert.That(PanelLayout.Hint.Bounds.xMin - PanelLayout.Task.Bounds.xMax, Is.GreaterThanOrEqualTo(.05f)); // RULING(orchestrator 2026-10-28): hint center x = +0.64 m.
    }
    [Test] public void PanelsFaceHeadOnYAxis()
    {
        var head = new GameObject("head"); var panel = new GameObject("panel");
        try {
            head.transform.position = new Vector3(2, 3, -1); panel.transform.position = new Vector3(0, 1, 0);
            var billboard = panel.AddComponent<PanelBillboard>(); billboard.Initialize(head.transform);
            var towards = head.transform.position - panel.transform.position; towards.y = 0;
            Assert.That(Vector3.Dot(-panel.transform.forward, towards.normalized), Is.GreaterThan(.999f));
            Assert.That(Vector3.Dot(panel.transform.up, Vector3.up), Is.GreaterThan(.999f));
        } finally { Object.DestroyImmediate(panel); Object.DestroyImmediate(head); }
    }
    [Test] public void BuiltPanelsFaceHeadAndUseTheirSlots()
    {
        var head = new GameObject("head"); var area = new GameObject("area");
        try {
            head.transform.position = new Vector3(2, 2, -2);
            var caption = new GameObject("caption"); caption.transform.SetParent(area.transform); var c = caption.AddComponent<CaptionPanel>(); c.Initialize(head.transform);
            var you = new GameObject("you"); you.transform.SetParent(area.transform); var y = you.AddComponent<YouSaidLine>(); y.Initialize(head.transform);
            var task = new GameObject("task"); task.AddComponent<TaskCard>().Initialize(area.transform, null, head.transform);
            var hint = new GameObject("hint"); hint.AddComponent<HintCard>().Initialize(area.transform, null, head.transform);
            var status = new GameObject("status"); status.AddComponent<StatusPanel>().Initialize(head.transform, null, area.transform);
            typeof(CaptionPanel).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, null);
            typeof(YouSaidLine).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(y, null);
            var panels = new[] { caption, you, task, hint, status };
            var slots = new[] { PanelLayout.Caption, PanelLayout.YouSaid, PanelLayout.Task, PanelLayout.Hint, PanelLayout.Status };
            for (int i = 0; i < panels.Length; i++) {
                Assert.That(Vector3.Distance(panels[i].transform.localPosition, slots[i].Centre), Is.LessThan(.0001f));
                var direction = head.transform.position - panels[i].transform.position; direction.y = 0;
                Assert.That(Vector3.Dot(-panels[i].transform.forward, direction.normalized), Is.GreaterThan(.999f), panels[i].name);
                Assert.That(Vector3.Dot(panels[i].transform.up, Vector3.up), Is.GreaterThan(.999f), panels[i].name);
            }
        } finally { Object.DestroyImmediate(area); Object.DestroyImmediate(head); }
    }
}

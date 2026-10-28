using System.Linq;
using NUnit.Framework;
using Sensei.App;
using TMPro;
using UnityEngine;

public sealed class MenuPanelTests
{
    [Test] public void MenuHasAllVisibleButtonLabels()
    {
        var root = new GameObject("menu"); var hand = new GameObject("hand"); var head = new GameObject("head"); var owner = new GameObject("host");
        try
        {
            var panel = root.AddComponent<MenuPanel>(); panel.Initialize(hand.transform, head.transform, owner.AddComponent<EngineHost>(), null);
            var buttons = root.GetComponentsInChildren<MenuButton>(true);
            Assert.AreEqual(8, buttons.Length);
            var backgroundQueue = root.transform.Find("Menu background").GetComponent<Renderer>().sharedMaterial.renderQueue;
            Assert.That(buttons.All(b => b.transform.Find("Button background").GetComponent<Renderer>().sharedMaterial.renderQueue > backgroundQueue));
            CollectionAssert.AreEquivalent(new[] { "I know no Japanese", "A few words", "Beginner", "Intermediate", "Advanced", "Explain in English", "Session summary", "Reset progress" }, buttons.Select(b => b.GetComponentInChildren<TextMeshPro>(true).text));
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(hand); Object.DestroyImmediate(head); Object.DestroyImmediate(owner); }
    }
}

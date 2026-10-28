using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Sensei.App;
using Sensei.App.Core;
using Sensei.Engine.World;
using UnityEngine;
using UnityEngine.TestTools;

public class ReviewU1Tests
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Field(object o, string name, object value) => o.GetType().GetField(name, Private).SetValue(o, value);
    static object Field(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);
    static void Call(object o, string name) => o.GetType().GetMethod(name, Private).Invoke(o, null);
    static SceneSnapshot Snapshot(long revision, string region) => new SceneSnapshot { Revision = revision, Instances = new List<SnapshotInstance> { new SnapshotInstance { Id = "apple-1", CatalogId = "apple", Region = region, Slot = 0 } } };

    [Test] public void OlderSceneNotificationCannotOverwriteNewerRevision()
    {
        LogAssert.ignoreFailingMessages = true;
        var root = new GameObject("area"); var head = new GameObject("head"); var hand = new GameObject("hand");
        try {
            var area = root.AddComponent<PracticeAreaView>(); area.Initialize(head.transform, hand.transform);
            var queue = new MainThreadQueue(); var events = new UnityEngineEvents(queue, area, null, _ => {});
            // Independent engine operations can flush outside the lock in this order.
            events.OnSceneChanged(new SceneEvent { Revision = 2 }, Snapshot(2, "right"));
            events.OnSceneChanged(new SceneEvent { Revision = 1 }, Snapshot(1, "left"));
            queue.Drain();
            var target = (Vector3)Field(area.Find("apple-1"), "target");
            Assert.That(target.x, Is.EqualTo(0.45f).Within(0.0001f), "revision 1 must not replace revision 2");
        } finally { Object.DestroyImmediate(root); Object.DestroyImmediate(head); Object.DestroyImmediate(hand); LogAssert.ignoreFailingMessages = false; }
    }

    [Test] public void GhostShaderActuallyFadesInEditorGpu()
    {
        var mat = new Material(Shader.Find("Sensei/GhostFade"));
        var rt = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var old = RenderTexture.active;
        try {
            RenderTexture.active = rt; GL.Clear(true, true, Color.black);
            Graphics.Blit(Texture2D.whiteTexture, rt, mat);
            RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
            var bottom = pixels.GetPixel(32, 3).r; var middle = pixels.GetPixel(32, 32).r;
            TestContext.WriteLine($"bottom={bottom:F3} middle={middle:F3}");
            Assert.That(bottom, Is.LessThan(middle * 0.8f), "ghost shader must fade its bottom");
        } finally { RenderTexture.active = old; Object.DestroyImmediate(mat); Object.DestroyImmediate(rt); Object.DestroyImmediate(pixels); }
    }

    [Test, Ignore("waits for sensei pose art batch RPS4")] public void EverySenseiPoseHasAnActualTexture()
    {
        var missing = new List<string>();
        foreach (var pose in new[] { "idle_float", "listen", "think", "speak", "point", "encourage", "demonstrate" })
            if (Resources.Load<Texture2D>("Sensei/" + pose + "_card") == null && Resources.Load<Texture2D>("Sensei/" + pose) == null) missing.Add(pose);
        Assert.That(missing, Is.Empty, "each U12 pose must have an actual image instead of the idle fallback");
    }

    [Test] public void CardRemainsAtDropUntilEngineMoveCompletes()
    {
        var go = new GameObject("card"); var head = new GameObject("head");
        try {
            var card = go.AddComponent<ObjectCardView>();
            Field(card, "head", head.transform); Field(card, "from", new Vector3(-0.45f, 0.8f, 0));
            Field(card, "target", new Vector3(-0.45f, 0.8f, 0)); Field(card, "changedAt", Time.time - 1f);
            go.transform.localPosition = new Vector3(0.45f, 0.8f, 0);
            card.Dragging = false; // ControllerInput.Release clears this before asynchronous PlayerMove finishes.
            Call(card, "Update");
            Assert.That(go.transform.localPosition.x, Is.EqualTo(0.45f).Within(0.0001f), "do not snap to old engine position while the move is pending");
        } finally { Object.DestroyImmediate(go); Object.DestroyImmediate(head); }
    }
}

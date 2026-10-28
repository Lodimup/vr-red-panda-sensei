using System.Reflection;
using NUnit.Framework;
using Sensei.App;
using UnityEngine;
using UnityEngine.TestTools;

public class PendingMoveTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [TestCase(false)] [TestCase(true)]
    public void AcceptedMoveWaitsForMatchingSceneInEitherOrder(bool sceneFirst)
    {
        LogAssert.ignoreFailingMessages = true;
        var root = new GameObject("root"); var head = new GameObject("head"); var go = new GameObject("card");
        try
        {
            var card = go.AddComponent<ObjectCardView>(); card.Initialize("a", "apple", head.transform);
            card.SetTarget(root.transform, Vector3.left, 1, false, "left", null);
            card.Dragging = true; go.transform.position = Vector3.right; card.Dragging = false;
            card.ExpectDestination("right", null); var version = card.PendingVersion;
            if (sceneFirst) card.SetTarget(root.transform, Vector3.right, 1, false, "right", null);
            else card.SetTarget(root.transform, Vector3.left, 1, false, "left", null);
            card.CompleteMove(version, true);
            Assert.That((bool)typeof(ObjectCardView).GetField("pending", Private).GetValue(card), Is.EqualTo(!sceneFirst));
            if (!sceneFirst) card.SetTarget(root.transform, Vector3.right, 1, false, "right", null);
            Assert.That((bool)typeof(ObjectCardView).GetField("pending", Private).GetValue(card), Is.False);
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(root); Object.DestroyImmediate(head); LogAssert.ignoreFailingMessages = false; }
    }
    [TestCase(false)] [TestCase(true)]
    public void RejectedOrTimedOutMoveReturnsToLastTarget(bool timeout)
    {
        var go = new GameObject("card"); var head = new GameObject("head");
        try
        {
            var card = go.AddComponent<ObjectCardView>();
            typeof(ObjectCardView).GetField("head", Private).SetValue(card, head.transform);
            typeof(ObjectCardView).GetField("target", Private).SetValue(card, Vector3.left);
            go.transform.localPosition = Vector3.right; card.Dragging = false;
            if (timeout) { typeof(ObjectCardView).GetField("pendingAt", Private).SetValue(card, Time.time - 4f); typeof(ObjectCardView).GetMethod("Update", Private).Invoke(card, null); }
            else card.CompleteMove(card.PendingVersion, false);
            Assert.That((bool)typeof(ObjectCardView).GetField("pending", Private).GetValue(card), Is.False);
            Assert.That((Vector3)typeof(ObjectCardView).GetField("target", Private).GetValue(card), Is.EqualTo(Vector3.left));
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(head); }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sensei.App.Core;
using Sensei.Engine.World;
using UnityEngine;

namespace Sensei.App.Tests
{
    public sealed class CoreTests
    {
        private static SnapshotInstance Item(string id, string region, int slot, string? container = null) => new SnapshotInstance { Id = id, CatalogId = "apple", Region = region, Slot = slot, ContainerId = container };
        [Test] public void RowsAndContainersAreIndependentlyCentered()
        {
            var scene = new SceneSnapshot { Instances = new List<SnapshotInstance> { Item("four", "center", 4), Item("one", "center", 1), Item("three", "center", 3), Item("two", "center", 2), Item("five", "center", 5), Item("child2", "center", 2, "one"), Item("child1", "center", 1, "one"), Item("hand", "learner_hand", 1) } };
            var placements = PracticeLayout.Compute(scene).ToDictionary(x => x.InstanceId);
            Assert.That(placements["one"].LocalPosition.x, Is.EqualTo(-0.24f).Within(0.0001));
            Assert.That(placements["four"].LocalPosition.x, Is.EqualTo(0.24f).Within(0.0001));
            Assert.That(placements["five"].LocalPosition, Is.EqualTo(new Vector3(0, 0.8f, 0.14f)));
            Assert.That(placements["child1"].LocalPosition.x, Is.EqualTo(-0.265f).Within(0.0001));
            Assert.That(placements["child2"].LocalPosition.x, Is.EqualTo(-0.215f).Within(0.0001));
            Assert.That(placements["child1"].Scale, Is.EqualTo(0.6f)); Assert.That(placements["child1"].ContainerId, Is.EqualTo("one"));
            Assert.That(placements["hand"].LocalPosition, Is.EqualTo(Vector3.zero));
            Assert.That(PracticeLayout.Compute(scene).Select(x => x.InstanceId), Is.EqualTo(PracticeLayout.Compute(scene).Select(x => x.InstanceId)));
        }
        [Test] public void RegionBoundariesAndCentres()
        {
            Assert.That(PracticeLayout.RegionCentre("sensei_side"), Is.EqualTo(new Vector3(-0.95f, 0.8f, 0.15f)));
            Assert.That(PracticeLayout.RegionAt(new Vector3(-0.225f, 0.75f, -0.25f)), Is.EqualTo("left"));
            Assert.That(PracticeLayout.RegionAt(new Vector3(0.225f, 1.1f, 0.25f)), Is.EqualTo("center"));
            Assert.That(PracticeLayout.RegionAt(new Vector3(0.226f, 0.8f, 0)), Is.EqualTo("right"));
            Assert.That(PracticeLayout.RegionAt(new Vector3(0, 1.101f, 0)), Is.Null);
        }
        [Test] public void PosePriorityAndExpiration()
        {
            var state = new PoseState(); state.OnAnimate("point", 10); Assert.That(state.Current(12.99), Is.EqualTo(SenseiPose.Point));
            state.SetEngineState("speaking"); Assert.That(state.Current(11), Is.EqualTo(SenseiPose.Speak));
            state.SetTalkHeld(true); Assert.That(state.Current(11), Is.EqualTo(SenseiPose.Listen));
            state.SetTalkHeld(false); state.SetEngineState("thinking"); Assert.That(state.Current(11), Is.EqualTo(SenseiPose.Think));
            state.SetEngineState("idle"); Assert.That(state.Current(13), Is.EqualTo(SenseiPose.IdleFloat));
        }
        [Test] public void QueueRunsOnDrainingThreadAndHonorsLimit()
        {
            var queue = new MainThreadQueue(); var seen = new List<int>(); Task.Run(() => { queue.Post(() => seen.Add(1)); queue.Post(() => seen.Add(2)); }).Wait();
            Assert.That(queue.Drain(1), Is.EqualTo(1)); Assert.That(seen, Is.EqualTo(new[] { 1 })); Assert.That(queue.Drain(), Is.EqualTo(1)); Assert.That(seen, Is.EqualTo(new[] { 1, 2 }));
        }
        [TestCase(0.29f, "hello", false)] [TestCase(0.3f, "hello", true)] [TestCase(1f, "  ", false)] [TestCase(1f, null, false)]
        public void TranscriptFiltering(float duration, string? text, bool expected) => Assert.That(TranscriptGate.ShouldSend(duration, text), Is.EqualTo(expected));
        [TestCase("failed", "auth_failed", "The API key was rejected.")] [TestCase("failed", "quota_exceeded", "The model quota is used up.")] [TestCase("failed", "network_error", "No connection to the model.")] [TestCase("failed", "provider_error", "The model service had an error.")] [TestCase("failed", "other", "Something went wrong. Try again.")] [TestCase("completed", null, null)] [TestCase("budget_exhausted", null, null)] [TestCase("failed", "budget_exhausted", null)]
        public void TurnMessagesMatchErrors(string status, string? code, string? expected) => Assert.That(TurnMessages.ForTurn(status, code), Is.EqualTo(expected));
    }
}

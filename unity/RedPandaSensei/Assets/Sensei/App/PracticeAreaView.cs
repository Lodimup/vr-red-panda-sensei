#nullable enable
using System.Collections.Generic;
using System.Linq;
using Sensei.App.Core;
using Sensei.Engine.World;
using UnityEngine;

namespace Sensei.App
{
    public sealed class PracticeAreaView : MonoBehaviour
    {
        private readonly Dictionary<string, ObjectCardView> cards = new Dictionary<string, ObjectCardView>();
        private Transform head = null!;
        private Transform hand = null!;
        private GameObject mat = null!;
        public void Initialize(Transform headTransform, Transform handTransform)
        {
            head = headTransform; hand = handTransform;
            mat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mat.name = "Practice tray"; mat.transform.SetParent(transform, false);
            mat.transform.localPosition = new Vector3(0, 0.79f, 0); mat.transform.localScale = new Vector3(1.4f, 0.02f, 0.5f);
            var trayRenderer = mat.GetComponent<Renderer>();
            trayRenderer.material = new Material(Resources.Load<Material>("SenseiCard"));
            trayRenderer.material.color = new Color(0.56f, 0.36f, 0.19f);
            Destroy(mat.GetComponent<Collider>());
            foreach (var region in new[] { "left", "center", "right" })
            {
                var zone = new GameObject("Drop zone " + region); zone.transform.SetParent(transform, false);
                zone.transform.localPosition = PracticeLayout.RegionCentre(region) + new Vector3(0, 0.15f, 0);
                var box = zone.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(0.45f, 0.30f, 0.5f);
            }
        }
        public void Place()
        {
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            transform.position = new Vector3(head.position.x, 0, head.position.z) + forward * 0.9f;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
        public void Render(SceneSnapshot scene)
        {
            var placements = PracticeLayout.Compute(scene);
            var alive = new HashSet<string>(placements.Select(p => p.InstanceId));
            foreach (var id in cards.Keys.ToArray()) if (!alive.Contains(id)) { cards[id].Remove(); cards.Remove(id); }
            foreach (var p in placements)
            {
                if (!cards.TryGetValue(p.InstanceId, out var card))
                {
                    var go = new GameObject("Card " + p.InstanceId); card = go.AddComponent<ObjectCardView>();
                    card.Initialize(p.InstanceId, p.CatalogId, head); cards.Add(p.InstanceId, card);
                }
                var parent = p.Region == "learner_hand" ? hand : transform;
                var position = p.Region == "learner_hand" ? new Vector3(0, 0.02f, 0.08f) + p.LocalPosition : p.LocalPosition;
                card.SetTarget(parent, position, p.Scale, scene.SelectedId == p.InstanceId);
            }
        }
        public void Highlight(IEnumerable<string> ids, int durationMs) { foreach (var id in ids) if (cards.TryGetValue(id, out var card)) card.Highlight(durationMs); }
        public ObjectCardView? Find(string id) => cards.TryGetValue(id, out var card) ? card : null;
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Sensei.Engine.World;
using UnityEngine;

namespace Sensei.App.Core
{
    public readonly struct Placement
    {
        public string InstanceId { get; }
        public string CatalogId { get; }
        public string Region { get; }
        public string? ContainerId { get; }
        public Vector3 LocalPosition { get; }
        public float Scale { get; }
        public Placement(string id, string catalogId, string region, string? containerId, Vector3 localPosition, float scale)
        { InstanceId = id; CatalogId = catalogId; Region = region; ContainerId = containerId; LocalPosition = localPosition; Scale = scale; }
    }

    public static class PracticeLayout
    {
        public const float MatY = 0.80f, RegionSpacing = 0.45f, SlotSpacing = 0.16f, RowDepth = 0.14f;
        public static Vector3 RegionCentre(string region)
        {
            switch (region)
            {
                case "left": return new Vector3(-RegionSpacing, MatY, 0);
                case "center": return new Vector3(0, MatY, 0);
                case "right": return new Vector3(RegionSpacing, MatY, 0);
                case "sensei_side": return new Vector3(-0.95f, MatY, 0.15f);
                case "learner_hand": return Vector3.zero;
                default: throw new ArgumentException("Unknown region", nameof(region));
            }
        }
        public static string? RegionAt(Vector3 point)
        {
            if (point.y < 0.75f || point.y > 1.10f || point.z < -0.25f || point.z > 0.25f || point.x < -0.675f || point.x > 0.675f) return null;
            if (point.x <= -0.225f) return "left";
            if (point.x <= 0.225f) return "center";
            return "right";
        }
        public static IReadOnlyList<Placement> Compute(SceneSnapshot scene)
        {
            var result = new List<Placement>();
            var positions = new Dictionary<string, Vector3>();
            var roots = scene.Instances.Where(x => x.ContainerId == null).GroupBy(x => x.Region).OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var group in roots)
            {
                var items = group.OrderBy(x => x.Slot).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
                for (var i = 0; i < items.Length; i++)
                {
                    var row = i < 4 ? 0 : 1; var rowStart = row * 4; var n = row == 0 ? Math.Min(4, items.Length) : items.Length - 4;
                    var position = group.Key == "learner_hand" ? Vector3.zero : RegionCentre(group.Key) + new Vector3((i - rowStart - (n - 1) / 2f) * SlotSpacing, 0, row * RowDepth);
                    var item = items[i]; positions[item.Id] = position;
                    result.Add(new Placement(item.Id, item.CatalogId, item.Region, null, position, 1));
                }
            }
            foreach (var group in scene.Instances.Where(x => x.ContainerId != null).GroupBy(x => x.ContainerId!).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (!positions.TryGetValue(group.Key, out var origin)) continue;
                var items = group.OrderBy(x => x.Slot).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
                for (var i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    var p = item.Region == "learner_hand" ? Vector3.zero : origin + new Vector3((i - (items.Length - 1) / 2f) * 0.05f, 0.06f, -0.03f);
                    result.Add(new Placement(item.Id, item.CatalogId, item.Region, item.ContainerId, p, 0.6f));
                }
            }
            return result;
        }
    }
}

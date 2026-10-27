using System.Collections.Generic;
using System.Linq;
using Sensei.Engine.World;

namespace Sensei.Engine.Tasks;

/// <summary>Evaluates task predicates against the scene (SPEC §7 table, S35d). Use under the engine lock.</summary>
public static class PredicateEvaluator
{
    /// <summary>True when every predicate of <paramref name="task"/> holds.</summary>
    public static bool AllTrue(TaskView task, SceneWorld world) => task.Predicates.All(p => IsTrue(p, task, world));

    public static bool IsTrue(TaskPredicate p, TaskView task, SceneWorld world)
    {
        switch (p.Type)
        {
            case PredicateTypes.Selected:
                return p.ObjectId is not null && world.SelectedId == p.ObjectId;

            case PredicateTypes.InRegion:
                return Subjects(p, task, world).Any(i => i.Region == p.Region && i.ContainerId is null);

            case PredicateTypes.ContainedIn:
                return p.ContainerId is not null && Subjects(p, task, world).Any(i => i.ContainerId == p.ContainerId);

            case PredicateTypes.Quantity:
            {
                if (p.Selector is null || p.Count is null) return false;
                var matches = world.Instances.Where(i => Matches(i, p.Selector, task.ScenarioProperties));
                var count = p.ContainerId is not null
                    ? matches.Count(i => i.ContainerId == p.ContainerId)
                    : matches.Count(i => i.Region == p.Region && i.ContainerId is null);
                return (p.Comparison ?? QuantityComparisons.Eq) switch
                {
                    QuantityComparisons.Gte => count >= p.Count.Value,
                    QuantityComparisons.Lte => count <= p.Count.Value,
                    _ => count == p.Count.Value,
                };
            }

            case PredicateTypes.LeftOf:
            case PredicateTypes.RightOf:
            {
                var a = world.Find(p.ObjectId);
                var b = world.Find(p.ReferenceId);
                if (a is null || b is null) return false;
                if (a.ContainerId is not null || b.ContainerId is not null) return false;
                var ai = SceneRegions.PracticeIndex(a.Region);
                var bi = SceneRegions.PracticeIndex(b.Region);
                if (ai < 0 || bi < 0) return false;
                var cmp = ai != bi ? ai.CompareTo(bi) : a.Slot.CompareTo(b.Slot);
                return p.Type == PredicateTypes.LeftOf ? cmp < 0 : cmp > 0;
            }

            case PredicateTypes.Unchanged:
                return p.ObjectId is not null && world.Exists(p.ObjectId) &&
                       !world.EventsAfter(task.PredicatesSinceEventId).Any(e =>
                           (e.Kind == SceneEventKinds.Moved || e.Kind == SceneEventKinds.Removed) && e.InstanceIds.Contains(p.ObjectId));

            case PredicateTypes.Before:
            {
                if (p.ObjectId is null || p.ReferenceId is null) return false;
                var first = FirstMove(p.ObjectId, task, world);
                var reference = FirstMove(p.ReferenceId, task, world);
                return first is not null && reference is not null && first.Value < reference.Value;
            }

            default:
                return false;
        }
    }

    /// <summary>Does <paramref name="instance"/> match <paramref name="selector"/> (catalog id, variant, scenario property)?</summary>
    public static bool Matches(ObjectInstance instance, PredicateSelector selector, IReadOnlyList<ScenarioProperty> scenarioProperties)
    {
        if (instance.CatalogId != selector.CatalogId) return false;
        if (selector.Variant is not null && instance.Variant != selector.Variant) return false;
        if (selector.Property is not null &&
            !scenarioProperties.Any(sp => sp.ObjectId == instance.Id && sp.Name == selector.Property.Name && sp.Value == selector.Property.Value))
            return false;
        return true;
    }

    /// <summary>Instances matching <paramref name="selector"/> in the whole scene.</summary>
    public static IEnumerable<ObjectInstance> MatchesInScene(PredicateSelector selector, IReadOnlyList<ScenarioProperty> scenarioProperties, IEnumerable<ObjectInstance> instances) =>
        instances.Where(i => Matches(i, selector, scenarioProperties));

    private static IEnumerable<ObjectInstance> Subjects(TaskPredicate p, TaskView task, SceneWorld world)
    {
        if (p.ObjectId is not null)
        {
            var i = world.Find(p.ObjectId);
            return i is null ? Enumerable.Empty<ObjectInstance>() : new[] { i };
        }
        if (p.Selector is not null) return MatchesInScene(p.Selector, task.ScenarioProperties, world.Instances);
        return Enumerable.Empty<ObjectInstance>();
    }

    private static long? FirstMove(string id, TaskView task, SceneWorld world) =>
        world.EventsAfter(task.PredicatesSinceEventId)
            .FirstOrDefault(e => e.Kind == SceneEventKinds.Moved && e.InstanceIds.Contains(id))?.EventId;
}

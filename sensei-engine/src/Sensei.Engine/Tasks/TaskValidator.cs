using System;
using System.Collections.Generic;
using System.Linq;
using Sensei.Engine.Catalog;
using Sensei.Engine.Tools;
using Sensei.Engine.World;

namespace Sensei.Engine.Tasks;

/// <summary>
/// Task validation shared by <c>start_challenge</c> and <c>update_challenge</c>.
/// <see cref="ValidateShape"/> is argument validation (<c>invalid_arguments</c>, S21/S35a, predicate fields by type);
/// <see cref="ValidateFeasibility"/> is the semantic <c>impossible_task</c> check (S35b, S104, S106), used under the engine lock.
/// </summary>
public static class TaskValidator
{
    private static readonly string[] PredicateFields = { "object_id", "selector", "region", "container_id", "count", "comparison", "reference_id" };

    /// <summary>
    /// Instruction rule (S35a: at most one language absent) and predicate fields by type (SPEC §7 table).
    /// Pass null for a part the call does not carry. Null when valid, else an <c>invalid_arguments</c> detail.
    /// </summary>
    public static string? ValidateShape(TaskInstruction? instruction, IReadOnlyList<TaskPredicate>? predicates, bool instructionRequired = true)
    {
        if (instruction is null)
        {
            if (instructionRequired) return "instruction: required property is missing";
        }
        else if (instruction.Japanese is null && instruction.English is null)
        {
            return "instruction: at least one of japanese or english is required";
        }

        if (predicates is null) return null;
        for (var i = 0; i < predicates.Count; i++)
        {
            var detail = ValidatePredicateShape(predicates[i], $"predicates[{i}]");
            if (detail is not null) return detail;
        }
        return null;
    }

    /// <summary>Fields allowed and required for one predicate type. Null when valid.</summary>
    public static string? ValidatePredicateShape(TaskPredicate p, string path)
    {
        if (!PredicateTypes.All.Contains(p.Type, StringComparer.Ordinal))
            return $"{path}.type: must be one of {string.Join(", ", PredicateTypes.All)}";

        HashSet<string> allowed;
        switch (p.Type)
        {
            case PredicateTypes.Selected:
            case PredicateTypes.Unchanged:
                allowed = new HashSet<string> { "object_id" };
                if (p.ObjectId is null) return Missing(path, "object_id", p.Type);
                break;
            case PredicateTypes.InRegion:
                allowed = new HashSet<string> { "object_id", "selector", "region" };
                if ((p.ObjectId is null) == (p.Selector is null)) return $"{path}: {p.Type} needs exactly one of object_id or selector";
                if (p.Region is null) return Missing(path, "region", p.Type);
                break;
            case PredicateTypes.ContainedIn:
                allowed = new HashSet<string> { "object_id", "selector", "container_id" };
                if ((p.ObjectId is null) == (p.Selector is null)) return $"{path}: {p.Type} needs exactly one of object_id or selector";
                if (p.ContainerId is null) return Missing(path, "container_id", p.Type);
                break;
            case PredicateTypes.Quantity:
                allowed = new HashSet<string> { "selector", "region", "container_id", "count", "comparison" };
                if (p.Selector is null) return Missing(path, "selector", p.Type);
                if ((p.Region is null) == (p.ContainerId is null)) return $"{path}: quantity needs exactly one of region or container_id";
                if (p.Count is null) return Missing(path, "count", p.Type);
                break;
            default: // left_of, right_of, before
                allowed = new HashSet<string> { "object_id", "reference_id" };
                if (p.ObjectId is null) return Missing(path, "object_id", p.Type);
                if (p.ReferenceId is null) return Missing(path, "reference_id", p.Type);
                break;
        }

        foreach (var field in PredicateFields)
            if (IsSet(p, field) && !allowed.Contains(field)) return $"{path}.{field}: not allowed for type {p.Type}";

        if (p.Region is not null && !SceneRegions.IsRegion(p.Region))
            return $"{path}.region: must be one of {string.Join(", ", SceneRegions.All)}";
        if (p.Comparison is not null && !QuantityComparisons.All.Contains(p.Comparison, StringComparer.Ordinal))
            return $"{path}.comparison: must be one of {string.Join(", ", QuantityComparisons.All)}";
        if (p.Count is not null && (p.Count < 1 || p.Count > 8))
            return $"{path}.count: must be between 1 and 8";
        return null;
    }

    /// <summary>
    /// <c>impossible_task</c> checks in story order: S35b, then S104 (a non-<c>unchanged</c> predicate), then S106.
    /// Use under the engine lock. Null when the task is possible.
    /// </summary>
    public static string? ValidateFeasibility(SceneWorld world, IReadOnlyList<TaskPredicate> predicates, IReadOnlyList<ScenarioProperty> scenarioProperties)
    {
        // S35b: referenced objects and containers exist; container_id names a container.
        for (var i = 0; i < predicates.Count; i++)
        {
            var p = predicates[i];
            var path = $"predicates[{i}]";
            if (p.ObjectId is not null && !world.Exists(p.ObjectId)) return $"{path}.object_id: object '{p.ObjectId}' does not exist";
            if (p.ContainerId is not null)
            {
                if (!world.Exists(p.ContainerId)) return $"{path}.container_id: object '{p.ContainerId}' does not exist";
                if (!world.IsContainer(p.ContainerId)) return $"{path}.container_id: '{p.ContainerId}' is not a container";
            }
            if (p.ReferenceId is not null && !world.Exists(p.ReferenceId)) return $"{path}.reference_id: object '{p.ReferenceId}' does not exist";
        }

        // S35b: object_id == reference_id.
        for (var i = 0; i < predicates.Count; i++)
        {
            var p = predicates[i];
            if (p.ObjectId is not null && p.ObjectId == p.ReferenceId) return $"predicates[{i}]: object_id and reference_id are the same object";
        }

        // S35b: selectors name catalog ids and declared properties.
        for (var i = 0; i < predicates.Count; i++)
        {
            var s = predicates[i].Selector;
            if (s is null) continue;
            var path = $"predicates[{i}].selector";
            if (!ObjectCatalog.Contains(s.CatalogId)) return $"{path}.catalog_id: '{s.CatalogId}' is not in the catalog";
            if (s.Property is not null && !scenarioProperties.Any(sp => sp.Name == s.Property.Name && sp.Value == s.Property.Value))
                return $"{path}.property: '{s.Property.Name}={s.Property.Value}' is not declared in scenario_properties";
        }

        // S35b: quantity eq/gte cannot exceed matching instances in the whole scene.
        for (var i = 0; i < predicates.Count; i++)
        {
            var p = predicates[i];
            if (p.Type != PredicateTypes.Quantity || p.Selector is null || p.Count is null) continue;
            var comparison = p.Comparison ?? QuantityComparisons.Eq;
            if (comparison == QuantityComparisons.Lte) continue;
            var available = PredicateEvaluator.MatchesInScene(p.Selector, scenarioProperties, world.Instances).Count();
            if (p.Count.Value > available)
                return $"predicates[{i}].count: needs {p.Count.Value} matching object(s) but the scene has {available}";
        }

        // S35b: every predicate already true at start (a task of only `unchanged` predicates is allowed here).
        if (predicates.Any(p => p.Type != PredicateTypes.Unchanged))
        {
            var probe = new TaskView
            {
                Predicates = predicates.ToList(),
                ScenarioProperties = scenarioProperties.ToList(),
                PredicatesSinceEventId = world.LastEventId,
            };
            if (PredicateEvaluator.AllTrue(probe, world)) return "predicates: every predicate is already true";
        }

        // S35b: scenario property objects exist.
        for (var i = 0; i < scenarioProperties.Count; i++)
        {
            var sp = scenarioProperties[i];
            if (!world.Exists(sp.ObjectId)) return $"scenario_properties[{i}].object_id: object '{sp.ObjectId}' does not exist";
        }

        // S104: at least one predicate that is not `unchanged`.
        if (predicates.All(p => p.Type == PredicateTypes.Unchanged))
            return "predicates: a task needs at least one predicate that is not unchanged";

        // S106: one object required in two different places.
        var places = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < predicates.Count; i++)
        {
            var p = predicates[i];
            if (p.ObjectId is null) continue;
            string? place = p.Type switch
            {
                PredicateTypes.InRegion => "region:" + p.Region,
                PredicateTypes.ContainedIn => "container:" + p.ContainerId,
                _ => null,
            };
            if (place is null) continue;
            if (places.TryGetValue(p.ObjectId, out var other) && other != place)
                return $"predicates[{i}]: '{p.ObjectId}' is required in two different places";
            places[p.ObjectId] = place;
        }

        // S106: contained_in names an object that is itself a container.
        for (var i = 0; i < predicates.Count; i++)
        {
            var p = predicates[i];
            if (p.Type != PredicateTypes.ContainedIn) continue;
            if (p.ObjectId is not null && world.IsContainer(p.ObjectId))
                return $"predicates[{i}].object_id: '{p.ObjectId}' is a container and cannot go into a container";
            if (p.Selector is not null && ObjectCatalog.IsContainer(p.Selector.CatalogId))
                return $"predicates[{i}].selector: {p.Selector.CatalogId} is a container and cannot go into a container";
        }

        // S106: contradictory order on the same pair (left_of/right_of, or two before).
        var spatial = new HashSet<(string, string)>();
        var temporal = new HashSet<(string, string)>();
        for (var i = 0; i < predicates.Count; i++)
        {
            var p = predicates[i];
            if (p.ObjectId is null || p.ReferenceId is null) continue;
            switch (p.Type)
            {
                case PredicateTypes.LeftOf:
                case PredicateTypes.RightOf:
                    var pair = p.Type == PredicateTypes.LeftOf ? (p.ObjectId, p.ReferenceId) : (p.ReferenceId, p.ObjectId);
                    if (spatial.Contains((pair.Item2, pair.Item1))) return $"predicates[{i}]: contradicts another left_of/right_of on the same pair";
                    spatial.Add(pair);
                    break;
                case PredicateTypes.Before:
                    if (temporal.Contains((p.ReferenceId, p.ObjectId))) return $"predicates[{i}]: contradicts another before on the same pair";
                    temporal.Add((p.ObjectId, p.ReferenceId));
                    break;
            }
        }

        // S106: a positional predicate names an object inside a container.
        for (var i = 0; i < predicates.Count; i++)
        {
            var p = predicates[i];
            if (p.Type != PredicateTypes.LeftOf && p.Type != PredicateTypes.RightOf) continue;
            if (world.Find(p.ObjectId)?.ContainerId is not null) return $"predicates[{i}].object_id: '{p.ObjectId}' is inside a container";
            if (world.Find(p.ReferenceId)?.ContainerId is not null) return $"predicates[{i}].reference_id: '{p.ReferenceId}' is inside a container";
        }

        return null;
    }

    private static string Missing(string path, string field, string type) => $"{path}.{field}: required for type {type}";

    private static bool IsSet(TaskPredicate p, string field) => field switch
    {
        "object_id" => p.ObjectId is not null,
        "selector" => p.Selector is not null,
        "region" => p.Region is not null,
        "container_id" => p.ContainerId is not null,
        "count" => p.Count is not null,
        "comparison" => p.Comparison is not null,
        "reference_id" => p.ReferenceId is not null,
        _ => false,
    };

    /// <summary>Instance ids a predicate list and scenario properties reference (for S84 and S32a).</summary>
    public static IEnumerable<string> ReferencedIds(IEnumerable<TaskPredicate>? predicates, IEnumerable<ScenarioProperty>? scenarioProperties = null)
    {
        foreach (var p in predicates ?? Enumerable.Empty<TaskPredicate>())
        {
            if (p.ObjectId is not null) yield return p.ObjectId;
            if (p.ReferenceId is not null) yield return p.ReferenceId;
            if (p.ContainerId is not null) yield return p.ContainerId;
        }
        foreach (var sp in scenarioProperties ?? Enumerable.Empty<ScenarioProperty>())
            yield return sp.ObjectId;
    }

    /// <summary>Failure helper for callers that want a <see cref="ToolFailure"/>.</summary>
    public static ToolFailure? AsImpossible(string? detail) => detail is null ? null : new ToolFailure(ToolErrorCodes.ImpossibleTask, detail);
}

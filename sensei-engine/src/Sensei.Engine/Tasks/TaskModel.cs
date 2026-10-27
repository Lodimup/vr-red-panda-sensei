using System.Collections.Generic;
using System.Text.Json.Serialization;
using Sensei.Engine.Learner;

namespace Sensei.Engine.Tasks;

/// <summary>A skill a task or attempt targets (SPEC §7, §9). Identity is <c>(dimension, key)</c> (S97).</summary>
public sealed class SkillTarget
{
    [JsonRequired]
    public string Dimension { get; set; } = "";

    [JsonRequired]
    public string Key { get; set; } = "";
    public string? Japanese { get; set; }

    public SkillTarget Clone() => (SkillTarget)MemberwiseClone();
}

/// <summary>Task instruction; at least one of the two languages (S35a).</summary>
public sealed class TaskInstruction
{
    public string? Japanese { get; set; }
    public string? English { get; set; }

    public TaskInstruction Clone() => (TaskInstruction)MemberwiseClone();
}

/// <summary>Scenario property attached to a scene object for this task (S35).</summary>
public sealed class ScenarioProperty
{
    public string ObjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";

    public ScenarioProperty Clone() => (ScenarioProperty)MemberwiseClone();
}

/// <summary><c>{ name, value }</c> inside a selector.</summary>
public sealed class SelectorProperty
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";

    public SelectorProperty Clone() => (SelectorProperty)MemberwiseClone();
}

/// <summary>Matches scene objects by catalog id and optional scenario property (S119: no variants).</summary>
public sealed class PredicateSelector
{
    public string CatalogId { get; set; } = "";
    public SelectorProperty? Property { get; set; }

    public PredicateSelector Clone()
    {
        var c = (PredicateSelector)MemberwiseClone();
        c.Property = Property?.Clone();
        return c;
    }
}

/// <summary>A task predicate (SPEC §7 table). <c>type</c> decides which fields are allowed.</summary>
public sealed class TaskPredicate
{
    public string Type { get; set; } = "";
    public string? ObjectId { get; set; }
    public PredicateSelector? Selector { get; set; }
    public string? Region { get; set; }
    public string? ContainerId { get; set; }
    public int? Count { get; set; }
    public string? Comparison { get; set; }
    public string? ReferenceId { get; set; }

    public TaskPredicate Clone()
    {
        var c = (TaskPredicate)MemberwiseClone();
        c.Selector = Selector?.Clone();
        return c;
    }
}

/// <summary>Predicate type names.</summary>
public static class PredicateTypes
{
    public const string Selected = "selected";
    public const string InRegion = "in_region";
    public const string ContainedIn = "contained_in";
    public const string Quantity = "quantity";
    public const string LeftOf = "left_of";
    public const string RightOf = "right_of";
    public const string Unchanged = "unchanged";
    public const string Before = "before";

    public static IReadOnlyList<string> All { get; } = new[] { Selected, InRegion, ContainedIn, Quantity, LeftOf, RightOf, Unchanged, Before };
}

/// <summary><c>quantity</c> comparisons.</summary>
public static class QuantityComparisons
{
    public const string Eq = "eq";
    public const string Gte = "gte";
    public const string Lte = "lte";

    public static IReadOnlyList<string> All { get; } = new[] { Eq, Gte, Lte };
}

/// <summary>Task families (SPEC §7).</summary>
public static class TaskFamilies
{
    public const string DiscoverRequest = "discover_request";
    public const string ListenArrange = "listen_arrange";
    public const string ConverseRoleplay = "converse_roleplay";

    public static IReadOnlyList<string> All { get; } = new[] { DiscoverRequest, ListenArrange, ConverseRoleplay };
}

/// <summary>Skill dimensions (SPEC §7, §9).</summary>
public static class SkillDimensions
{
    public const string Vocabulary = "vocabulary";
    public const string Listening = "listening";
    public const string Production = "production";
    public const string Reading = "reading";
    public const string Interaction = "interaction";
    public const string Register = "register";

    public static IReadOnlyList<string> All { get; } = new[] { Vocabulary, Listening, Production, Reading, Interaction, Register };
}

/// <summary>Task statuses.</summary>
public static class TaskStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Abandoned = "abandoned";
    public const string Ended = "ended";
}

/// <summary>A task as the model and the UI see it (S35c, S104, S108). Values handed out are deep copies.</summary>
public sealed class TaskView
{
    public string TaskId { get; set; } = "";
    public string Status { get; set; } = TaskStatuses.Active;
    public string Family { get; set; } = "";
    public List<SkillTarget> SkillTargets { get; set; } = new();
    public TaskInstruction Instruction { get; set; } = new();
    public List<TaskPredicate> Predicates { get; set; } = new();
    public List<ScenarioProperty> ScenarioProperties { get; set; } = new();
    public string? Role { get; set; }
    public bool IsFreshVariation { get; set; }
    public long SourceRevision { get; set; }

    /// <summary>The <c>task_started</c> event id; the assistance boundary (never moved by revise, S108).</summary>
    public long StartEventId { get; set; }

    /// <summary>Event id after which <c>unchanged</c>/<c>before</c> look for moves: start, or the latest predicate revise.</summary>
    public long PredicatesSinceEventId { get; set; }

    public SupportSettings AssistanceBaseline { get; set; } = new();
    public string? CompletedBy { get; set; }
    public long? CompletedEventId { get; set; }

    /// <summary>Interaction-sequence position of the <c>task_started</c> event (S94). Not part of the wire shape.</summary>
    [JsonIgnore]
    public long StartSequence { get; set; }

    /// <summary>Turn in which the task completed, was abandoned or ended (S96). Not part of the wire shape.</summary>
    [JsonIgnore]
    public long? ClosedTurnId { get; set; }

    public TaskView Clone()
    {
        var c = (TaskView)MemberwiseClone();
        c.SkillTargets = SkillTargets.ConvertAll(t => t.Clone());
        c.Instruction = Instruction.Clone();
        c.Predicates = Predicates.ConvertAll(p => p.Clone());
        c.ScenarioProperties = ScenarioProperties.ConvertAll(p => p.Clone());
        c.AssistanceBaseline = AssistanceBaseline.Clone();
        return c;
    }
}

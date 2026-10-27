using System.Collections.Generic;
using System.Text.Json.Serialization;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;

namespace Sensei.Engine.World;

/// <summary>One object in the scene (SPEC §5). Values returned to callers are copies.</summary>
public sealed class ObjectInstance
{
    public string Id { get; set; } = "";
    public string CatalogId { get; set; } = "";
    public string Region { get; set; } = "";
    public int Slot { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ContainerId { get; set; }

    public ObjectInstance Clone() => (ObjectInstance)MemberwiseClone();
}

/// <summary>A committed scene change (S17, S104).</summary>
public sealed class SceneEvent
{
    public long EventId { get; set; }
    public long Revision { get; set; }
    public string Actor { get; set; } = SceneActors.Sensei;
    public string Kind { get; set; } = "";
    public List<string> InstanceIds { get; set; } = new();
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Purpose { get; set; }
    public long TurnId { get; set; }

    /// <summary>Task id for task lifecycle events.</summary>
    public string? TaskId { get; set; }

    /// <summary>Position in the engine-wide interaction sequence (S92). Not part of the wire shape.</summary>
    [JsonIgnore]
    public long Sequence { get; set; }

    public SceneEvent Clone()
    {
        var c = (SceneEvent)MemberwiseClone();
        c.InstanceIds = new List<string>(InstanceIds);
        return c;
    }
}

/// <summary>An instance as shown in a <see cref="SceneSnapshot"/> (S20).</summary>
public sealed class SnapshotInstance
{
    public string Id { get; set; } = "";
    public string CatalogId { get; set; } = "";
    public string Region { get; set; } = "";
    public int Slot { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ContainerId { get; set; }

    public string Japanese { get; set; } = "";
    public string Reading { get; set; } = "";
    public string English { get; set; } = "";
}

/// <summary>Scene snapshot, also the <c>data</c> of <c>inspect_scene</c> (S20). Always a fresh copy.</summary>
public sealed class SceneSnapshot
{
    public long Revision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SelectedId { get; set; }

    public List<SnapshotInstance> Instances { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TaskView? ActiveTask { get; set; }

    public SupportSettings Support { get; set; } = new();
}

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sensei.Engine.Learner;

/// <summary>A deep copy of the learner profile, safe to keep (SPEC §3 <c>GetProfile</c>). Same fields as the stored JSON.</summary>
public sealed class LearnerProfileView
{
    public int Version { get; set; }
    public string ProfileId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? StartingBand { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SelfReportedJlpt { get; set; }

    public string WorkingLevel { get; set; } = "";
    public List<LevelChange> LevelHistory { get; set; } = new();
    public SupportSettings Support { get; set; } = new();
    public List<string> Pinned { get; set; } = new();
    public List<EvidenceRecord> Evidence { get; set; } = new();
    public Dictionary<string, string> Notes { get; set; } = new();
    public List<SessionRecord> Sessions { get; set; } = new();
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sensei.Engine.Learner;

/// <summary>The learner-facing session summary (S55). Never contains the working level.</summary>
public sealed class SessionSummary
{
    public string SessionId { get; set; } = "";
    public List<PracticedTarget> Practiced { get; set; } = new();
    public List<string> UnresolvedKeys { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? NextSuggestion { get; set; }

    public List<PreferenceChange> PreferenceChanges { get; set; } = new();
}

/// <summary>One practiced target in a <see cref="SessionSummary"/>.</summary>
public sealed class PracticedTarget
{
    public string Key { get; set; } = "";
    public string Dimension { get; set; } = "";
    public string Label { get; set; } = "";
    public int Independent { get; set; }
    public int Supported { get; set; }
    public int Unresolved { get; set; }
}

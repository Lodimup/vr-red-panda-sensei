using System.Collections.Generic;
using System.Text.Json.Serialization;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.World;

namespace Sensei.Engine.Events;

/// <summary>Events Unity renders from (SPEC §12). Raised after the change commits, on the calling thread (S69).</summary>
public interface IEngineEvents
{
    void OnSceneChanged(SceneEvent e, SceneSnapshot scene);
    void OnHighlight(IReadOnlyList<string> ids, int durationMs);
    void OnAnimate(string animation, string? targetId);
    void OnHint(HintCard card);
    void OnSupportChanged(SupportSettings settings);
    void OnTaskChanged(TaskView task);
    void OnSummary(SessionSummary summary);

    /// <summary><c>idle</c> | <c>thinking</c> | <c>speaking</c>.</summary>
    void OnSenseiState(string state);
}

/// <summary>A hint card shown to the learner (S38). Never names the working level.</summary>
public sealed class HintCard
{
    /// <summary><c>partial_phrase</c> | <c>model_answer</c> | <c>explanation</c> | <c>vocabulary_card</c> | <c>translation</c>.</summary>
    public string Kind { get; set; } = "";

    public string? Japanese { get; set; }
    public string? Reading { get; set; }
    public string? Romaji { get; set; }
    public string? English { get; set; }

    public string? TargetKey { get; set; }

    public HintCard Clone() => (HintCard)MemberwiseClone();
}

/// <summary>Sensei presentation states (S69).</summary>
public static class SenseiStates
{
    public const string Idle = "idle";
    public const string Thinking = "thinking";
    public const string Speaking = "speaking";
}

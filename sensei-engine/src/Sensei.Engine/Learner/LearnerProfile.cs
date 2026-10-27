using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Sensei.Engine.Tasks;

namespace Sensei.Engine.Learner;

/// <summary>The persisted learner profile (S58). Never contains an API key or raw audio.</summary>
public sealed class LearnerProfile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string ProfileId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? StartingBand { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SelfReportedJlpt { get; set; }

    public string WorkingLevel { get; set; } = WorkingLevels.PreN5;
    public List<LevelChange> LevelHistory { get; set; } = new();
    public SupportSettings Support { get; set; } = new();
    public List<string> Pinned { get; set; } = new();
    public List<EvidenceRecord> Evidence { get; set; } = new();
    public Dictionary<string, string> Notes { get; set; } = new();
    public List<SessionRecord> Sessions { get; set; } = new();

    /// <summary>A new default profile (S7): version 1, no band, working level pre_n5, zero-band support.</summary>
    public static LearnerProfile CreateDefault(string profileId, DateTimeOffset now) => new()
    {
        ProfileId = profileId,
        CreatedAt = now,
        UpdatedAt = now,
        WorkingLevel = WorkingLevels.PreN5,
        Support = new SupportSettings(),
    };

    public bool IsPinned(string field) => Pinned.Contains(field);

    /// <summary>The next per-profile attempt id <c>a&lt;n&gt;</c> (S46).</summary>
    public string NextAttemptId() => "a" + (Evidence.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The session record for <paramref name="sessionId"/>, or null.</summary>
    public SessionRecord? FindSession(string sessionId) => Sessions.Find(s => s.SessionId == sessionId);
}

/// <summary>One entry of <c>level_history</c> (S51, S100).</summary>
public sealed class LevelChange
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; }

    /// <summary>Attempt ids that drove an evidence-driven change (S100); null for band/self-report/difficulty.</summary>
    public List<string>? EvidenceIds { get; set; }

    public bool Superseded { get; set; }
}

/// <summary>One <c>sessions</c> entry (S58).</summary>
public sealed class SessionRecord
{
    public string SessionId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public List<PreferenceChange> PreferenceChanges { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? NextSuggestion { get; set; }
}

/// <summary>A support/band change recorded in the session (S41, S55).</summary>
public sealed class PreferenceChange
{
    public string Field { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? From { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? To { get; set; }

    /// <summary><c>player</c> or <c>sensei</c>.</summary>
    public string By { get; set; } = "";
}

/// <summary>Committed evidence of one attempt (S46, S93, S96, S98).</summary>
public sealed class EvidenceRecord
{
    public string AttemptId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public long TurnId { get; set; }
    public SkillTarget Target { get; set; } = new();
    public string? TranscriptExcerpt { get; set; }
    public string? SourceUtteranceId { get; set; }
    public List<long> EventIds { get; set; } = new();
    public List<string> Actors { get; set; } = new();

    /// <summary><c>task_completed</c> | <c>task_active</c> | <c>no_task</c>.</summary>
    public string EngineOutcome { get; set; } = "";
    public string? TaskId { get; set; }
    public string LanguageAssessment { get; set; } = "";
    public bool CommunicativeSuccess { get; set; }
    public List<string> Assistance { get; set; } = new();
    public string Outcome { get; set; } = "";
    public bool IsFreshVariation { get; set; }
    public string Uncertainty { get; set; } = "low";
    public string AssessmentSource { get; set; } = "model";
    public string RuleVersion { get; set; } = "rules-v1";
    public bool Superseded { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Attempt outcomes (S45).</summary>
public static class AttemptOutcomes
{
    public const string Unresolved = "unresolved";
    public const string ConfirmedIncorrect = "confirmed_incorrect";
    public const string CompletedByDemonstration = "completed_by_demonstration";
    public const string SuccessAfterModel = "success_after_model";
    public const string SuccessAfterHint = "success_after_hint";
    public const string SuccessAfterRepetition = "success_after_repetition";
    public const string IndependentSuccess = "independent_success";
}

/// <summary>Language assessments a model may give (SPEC §9 record_attempt).</summary>
public static class LanguageAssessments
{
    public const string Correct = "correct";
    public const string MinorIssue = "minor_issue";
    public const string MeaningBlockingError = "meaning_blocking_error";
    public const string UnclearInput = "unclear_input";

    public static IReadOnlyList<string> All { get; } = new[] { Correct, MinorIssue, MeaningBlockingError, UnclearInput };
}

/// <summary>Engine outcome of an attempt's bound task (S46).</summary>
public static class EngineOutcomes
{
    public const string TaskCompleted = "task_completed";
    public const string TaskActive = "task_active";
    public const string NoTask = "no_task";
}

/// <summary>Target labels (S49).</summary>
public static class TargetLabels
{
    public const string Stable = "stable";
    public const string UsedIndependently = "used_independently";
    public const string UsedWithHelp = "used_with_help";
    public const string Practiced = "practiced";
}

/// <summary>Label summary of one target (S49, S53).</summary>
public sealed class TargetLabel
{
    public string Key { get; set; } = "";
    public string Dimension { get; set; } = "";
    public string Label { get; set; } = TargetLabels.Practiced;

    public string? LastOutcome { get; set; }
    public int Attempts { get; set; }
}

/// <summary>A per-target recommendation (S50).</summary>
public sealed class Recommendation
{
    public string Key { get; set; } = "";
    public string Dimension { get; set; } = "";

    /// <summary><c>step_up</c> or <c>offer_simpler</c>.</summary>
    public string Kind { get; set; } = "";
}

/// <summary>Recommendation kinds (S50).</summary>
public static class RecommendationKinds
{
    public const string StepUp = "step_up";
    public const string OfferSimpler = "offer_simpler";
}

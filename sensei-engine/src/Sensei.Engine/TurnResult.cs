using System.Collections.Generic;
using System.Text.Json.Serialization;
using Sensei.Engine.Speech;
using Sensei.Engine.Trace;

namespace Sensei.Engine;

/// <summary>Result of <c>RunTurnAsync</c>/<c>CorrectLastTranscriptAsync</c> (S67).</summary>
public sealed class TurnResult
{
    public long TurnId { get; set; }
    public long GenerationId { get; set; }

    /// <summary><c>completed</c> | <c>cancelled</c> | <c>failed</c> | <c>budget_exhausted</c>.</summary>
    public string Status { get; set; } = TurnStatuses.Completed;

    /// <summary><c>llm_not_configured</c>, <c>auth_failed</c>, <c>quota_exceeded</c>, <c>network_error</c>, <c>provider_error</c>, <c>nothing_to_correct</c>; null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ErrorCode { get; set; }

    public string? ErrorDetail { get; set; }

    /// <summary>Trace entries of this turn.</summary>
    public List<TraceEntry> ToolCalls { get; set; } = new();

    public List<SpokenUtterance> Spoken { get; set; } = new();

    /// <summary>Plain assistant text; trace only, never spoken or shown.</summary>
    public string? UnspokenText { get; set; }

    public int Rounds { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}

/// <summary>One utterance spoken during a turn.</summary>
public sealed class SpokenUtterance
{
    public string UtteranceId { get; set; } = "";
    public List<SpeechSegment> Segments { get; set; } = new();
    public string Outcome { get; set; } = "";
}

/// <summary>Turn statuses (S67).</summary>
public static class TurnStatuses
{
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
    public const string BudgetExhausted = "budget_exhausted";
}

/// <summary>Turn error codes (S67, S99).</summary>
public static class TurnErrorCodes
{
    public const string LlmNotConfigured = "llm_not_configured";
    public const string AuthFailed = "auth_failed";
    public const string QuotaExceeded = "quota_exceeded";
    public const string NetworkError = "network_error";
    public const string ProviderError = "provider_error";
    public const string NothingToCorrect = "nothing_to_correct";
}

/// <summary>Result of a player action (S18, S19).</summary>
public sealed class PlayerActionResult
{
    public bool Ok { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ErrorCode { get; set; }

    public string? Detail { get; set; }
    public long Revision { get; set; }
}

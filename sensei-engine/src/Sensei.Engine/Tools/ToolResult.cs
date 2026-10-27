using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sensei.Engine.Tools;

/// <summary>The result of a tool call, returned to the model as snake_case JSON (SPEC §6).</summary>
public sealed class ToolResult
{
    public string CallId { get; set; } = "";
    public string Tool { get; set; } = "";

    /// <summary><c>success</c> | <c>failure</c> | <c>cancelled</c>.</summary>
    public string Status { get; set; } = ToolStatuses.Success;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Detail { get; set; }

    /// <summary>Scene revision after the call.</summary>
    public long Revision { get; set; }

    public List<string> AffectedIds { get; set; } = new();

    /// <summary>Tool-specific object, or null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public JsonElement? Data { get; set; }

    [JsonIgnore]
    public bool IsSuccess => Status == ToolStatuses.Success;
}

/// <summary>Tool result statuses.</summary>
public static class ToolStatuses
{
    public const string Success = "success";
    public const string Failure = "failure";
    public const string Cancelled = "cancelled";
}

/// <summary>Error codes, exact strings (SPEC §6).</summary>
public static class ToolErrorCodes
{
    public const string InvalidArguments = "invalid_arguments";
    public const string UnknownTool = "unknown_tool";
    public const string UnknownObject = "unknown_object";
    public const string UnknownCatalogId = "unknown_catalog_id";
    public const string UnsupportedAsset = "unsupported_asset";
    public const string CapacityExceeded = "capacity_exceeded";
    public const string InvalidDestination = "invalid_destination";
    public const string StaleScene = "stale_scene";
    public const string ImpossibleTask = "impossible_task";
    public const string TaskAlreadyActive = "task_already_active";
    public const string NoActiveTask = "no_active_task";
    public const string ObjectInActiveTask = "object_in_active_task";
    public const string PreferencePinned = "preference_pinned";
    public const string SpeechLimitExceeded = "speech_limit_exceeded";
    public const string ExcerptNotInTranscript = "excerpt_not_in_transcript";
    public const string UnknownEvent = "unknown_event";
    public const string TurnBudgetExhausted = "turn_budget_exhausted";
    public const string NothingToReplay = "nothing_to_replay";
    public const string NoteLimitExceeded = "note_limit_exceeded";
    public const string LooksLikeCredential = "looks_like_credential";
    public const string SpeechFailed = "speech_failed";
    public const string NoTurn = "no_turn";

    /// <summary>Error code of a cancelled call (status <c>cancelled</c>).</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Trace-only code for an event handler that threw (S69, S86).</summary>
    public const string EventHandlerError = "event_handler_error";

    /// <summary>Trace-only code for an unexpected exception inside a tool body (the exception is rethrown).</summary>
    public const string InternalError = "internal_error";

    /// <summary>All 22 failure codes of SPEC §6, in SPEC order.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        InvalidArguments, UnknownTool, UnknownObject, UnknownCatalogId, UnsupportedAsset, CapacityExceeded,
        InvalidDestination, StaleScene, ImpossibleTask, TaskAlreadyActive, NoActiveTask, ObjectInActiveTask,
        PreferencePinned, SpeechLimitExceeded, ExcerptNotInTranscript, UnknownEvent, TurnBudgetExhausted,
        NothingToReplay, NoteLimitExceeded, LooksLikeCredential, SpeechFailed, NoTurn,
    };

    /// <summary>S25: every failure except these counts as a validation failure.</summary>
    public static bool CountsAsValidationFailure(string? code) =>
        code is not null && code != StaleScene && code != SpeechFailed && code != TurnBudgetExhausted && code != Cancelled;
}

/// <summary>Shared <c>detail</c> strings set by the pipeline.</summary>
public static class ToolDetails
{
    public const string NotImplemented = "not_implemented";
    public const string CallIdReused = "call_id_reused";
    public const string IdNotYetVisible = "id_not_yet_visible";
    public const string DuplicateId = "duplicate_id";
    public const string MalformedJson = "malformed JSON";
    public const string ReservedCallId = "call_id: ids starting with 'auto-' are reserved for the engine";
}

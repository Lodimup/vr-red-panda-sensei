using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Text;
using Sensei.Engine.World;

namespace Sensei.Engine.Tools.Learner;

/// <summary>Arguments of <c>record_attempt</c> (S43–S47, S93–S98).</summary>
public sealed class RecordAttemptArgs
{
    public SkillTarget Target { get; set; } = new();
    public string? TranscriptExcerpt { get; set; }
    public bool CommunicativeSuccess { get; set; }
    public string LanguageAssessment { get; set; } = "";
    public List<long> EventIds { get; set; } = new();
    public string? Note { get; set; }
    public bool IsFreshVariation { get; set; }
    public string? TaskId { get; set; }
    public string? Uncertainty { get; set; }
}

/// <summary><c>record_attempt</c>: commits evidence of one learner attempt.</summary>
public sealed class RecordAttemptTool : SenseiTool<RecordAttemptArgs>
{
    public override string Name => "record_attempt";

    public override string Description => "Record evidence of one learner attempt at a skill target, quoting the learner's words; the engine decides the outcome from the help given.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "target": {
              "type": "object",
              "description": "The skill target attempted.",
              "properties": {
                "dimension": { "type": "string", "enum": ["vocabulary", "listening", "production", "reading", "interaction", "register"], "description": "Skill dimension." },
                "key": { "type": "string", "minLength": 1, "maxLength": 40, "description": "Target key." },
                "japanese": { "type": "string", "maxLength": 30, "description": "Japanese form of the target." }
              },
              "required": ["dimension", "key"],
              "additionalProperties": false
            },
            "transcript_excerpt": { "type": "string", "minLength": 1, "maxLength": 120, "description": "The learner's exact words from this or the previous turn; may be omitted for listening targets." },
            "communicative_success": { "type": "boolean", "description": "Whether the learner got the meaning across or understood." },
            "language_assessment": { "type": "string", "enum": ["correct", "minor_issue", "meaning_blocking_error", "unclear_input"], "description": "Language quality of the attempt." },
            "event_ids": { "type": "array", "description": "Scene event ids that show the attempt.", "items": { "type": "integer", "description": "Scene event id." } },
            "note": { "type": "string", "maxLength": 160, "description": "Short note for later sessions." },
            "is_fresh_variation": { "type": "boolean", "description": "True when the attempt was in a new variation of the task." },
            "task_id": { "type": "string", "description": "Task the attempt belongs to; defaults to the active or just-finished task." },
            "uncertainty": { "type": "string", "enum": ["low", "high"], "description": "How sure the assessment is; defaults to low." }
          },
          "required": ["target", "communicative_success", "language_assessment"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<RecordAttemptArgs> ArgsTypeInfo => SenseiJsonContext.Default.RecordAttemptArgs;

    /// <summary>S95: detail when a listening attempt does not cite player events after the task started.</summary>
    public const string ListeningRequiresPlayerEventsDetail = "listening_requires_player_events";

    /// <summary>S95: detail of <c>unknown_event</c> for a cited event outside the attempt's window.</summary>
    public const string EventOutOfWindowDetail = "event_out_of_window";

    /// <summary>S120: <c>engine_outcome</c> of an attempt bound to a task whose status is <c>ended</c> or <c>abandoned</c>.</summary>
    public const string EngineOutcomeTaskEnded = "task_ended";

    public override string? ValidateArguments(RecordAttemptArgs args)
    {
        if (PronunciationClaimGuard.Check(args.Target.Key, args.Target.Japanese, args.TranscriptExcerpt, args.Note, args.TaskId))
            return PronunciationClaimGuard.Detail;
        // S95: a listening attempt must cite events; every other dimension needs the learner's words.
        if (args.Target.Dimension == SkillDimensions.Listening)
            return args.EventIds.Count == 0 ? ListeningRequiresPlayerEventsDetail : null;
        return args.TranscriptExcerpt is null ? "transcript_excerpt: required unless the target dimension is listening" : null;
    }

    public override async Task<ToolResult> ExecuteAsync(RecordAttemptArgs args, ToolContext context, CancellationToken ct)
    {
        var committed = false;
        var result = context.Locked(() => Commit(args, context, out committed));
        // Committed: save at once (S46) without the call's token, so a late cancel cannot turn a committed call into `cancelled` (S22).
        if (committed) await context.SaveProfileAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    /// <summary>Runs under the engine lock: validates (S43, S47, S77, S95, S96), classifies (S44, S45, S94) and commits (S46, S51).</summary>
    private ToolResult Commit(RecordAttemptArgs args, ToolContext context, out bool committed)
    {
        committed = false;
        var profile = context.Profile;
        var target = args.Target;

        // S47: one commit per (turn, dimension, key); a second call returns the first attempt's data and writes nothing,
        // so it is resolved before any text of this call is validated: none of it would be persisted.
        var existing = profile.Evidence.FirstOrDefault(e => !e.Superseded && e.SessionId == context.SessionId && e.TurnId == context.TurnId
                                                            && e.Target.Dimension == target.Dimension && e.Target.Key == target.Key);
        if (existing is not null) return context.Success(null, Data(existing, duplicate: true));

        // S77: every learner text field that would be persisted (the excerpt, the note and the target's key and Japanese) must not hold a credential.
        if (context.Secrets.Check(args.TranscriptExcerpt))
            return context.Failure(ToolErrorCodes.LooksLikeCredential, "transcript_excerpt: looks like a credential");
        if (context.Secrets.Check(args.Note))
            return context.Failure(ToolErrorCodes.LooksLikeCredential, "note: looks like a credential");
        if (context.Secrets.Check(target.Key))
            return context.Failure(ToolErrorCodes.LooksLikeCredential, "target.key: looks like a credential");
        if (context.Secrets.Check(target.Japanese))
            return context.Failure(ToolErrorCodes.LooksLikeCredential, "target.japanese: looks like a credential");

        // S43, S93: the source utterance is the latest learner utterance of this or the previous turn that holds the excerpt.
        LearnerUtterance? source = null;
        if (args.TranscriptExcerpt is not null)
        {
            var needle = TextRules.NormalizeForMatch(args.TranscriptExcerpt);
            if (needle.Length > 0)
                source = context.Utterances.CurrentOrPreviousTurn(context.TurnId)
                    .LastOrDefault(u => TextRules.NormalizeForMatch(u.Text).Contains(needle, StringComparison.Ordinal));
            if (source is null)
                return context.Failure(ToolErrorCodes.ExcerptNotInTranscript, "transcript_excerpt: not found in the learner's transcript of this or the previous turn");
        }

        // S96, S121: the bound task.
        var task = BindTask(context, args.TaskId, out var found);
        if (!found)
            return context.Failure(ToolErrorCodes.NoActiveTask, $"task_id: '{args.TaskId}' is not active and did not finish in this or the previous turn");

        // S43, S95: cited events.
        var eventIds = args.EventIds.Distinct().ToList();
        var events = new List<SceneEvent>();
        for (var i = 0; i < eventIds.Count; i++)
        {
            var found1 = context.World.FindEvent(eventIds[i]);
            if (found1 is null) return context.Failure(ToolErrorCodes.UnknownEvent, $"event_ids[{i}]: unknown event {eventIds[i]}");
            events.Add(found1);
        }
        if (target.Dimension == SkillDimensions.Listening
            && events.Any(e => e.Actor != SceneActors.Player || (task is not null && e.EventId <= task.StartEventId)))
            return context.Failure(ToolErrorCodes.InvalidArguments, ListeningRequiresPlayerEventsDetail);
        var previousLearnerUtterance = context.Utterances.PreviousBefore(context.Turn.Utterance.Sequence);
        for (var i = 0; i < events.Count; i++)
        {
            var outOfWindow = task is not null
                ? events[i].EventId < task.StartEventId
                : previousLearnerUtterance is not null && events[i].Sequence < previousLearnerUtterance.Sequence;
            if (outOfWindow) return context.Failure(ToolErrorCodes.UnknownEvent, EventOutOfWindowDetail);
        }

        // S94: aids made after the boundary and before the source; support visibility as of the source.
        long sourceSequence;
        SupportSettings support;
        if (source is not null)
        {
            sourceSequence = source.Sequence;
            support = source.SupportAtArrival;
        }
        else
        {
            // A listening attempt without an excerpt: the source position is the first cited event (S95).
            sourceSequence = events.Min(e => e.Sequence);
            support = context.Utterances.All.LastOrDefault(u => u.Sequence < sourceSequence)?.SupportAtArrival ?? profile.Support;
        }
        var previousAttempt = profile.Evidence.LastOrDefault(e => !e.Superseded && e.SessionId == context.SessionId
                                                                  && e.Target.Dimension == target.Dimension && e.Target.Key == target.Key);
        var boundary = Math.Max(task?.StartSequence ?? 0, previousAttempt is null ? 0 : SourcePosition(context, previousAttempt));
        var previousUtteranceSequence = context.Utterances.PreviousBefore(sourceSequence)?.Sequence ?? 0;
        var aids = EvidenceRules.CountedAids(target.Dimension, target.Key, boundary, sourceSequence, previousUtteranceSequence, support, context.Assistance.Records);
        var outcome = EvidenceRules.ClassifyOutcome(args.LanguageAssessment, args.CommunicativeSuccess, aids);

        // S46, S98: the evidence record. S122: the clock time is stored as is; S51's window is decided by evidence position, not by time.
        var at = context.Clock.GetUtcNow();
        var record = new EvidenceRecord
        {
            AttemptId = profile.NextAttemptId(),
            SessionId = context.SessionId,
            TurnId = context.TurnId,
            Target = target.Clone(),
            TranscriptExcerpt = args.TranscriptExcerpt,
            SourceUtteranceId = source?.Id,
            EventIds = eventIds,
            Actors = events.Select(e => e.Actor).ToList(),
            EngineOutcome = EngineOutcomeOf(task),
            TaskId = task?.TaskId,
            LanguageAssessment = args.LanguageAssessment,
            CommunicativeSuccess = args.CommunicativeSuccess,
            Assistance = aids.ToList(),
            Outcome = outcome,
            IsFreshVariation = args.IsFreshVariation,
            Uncertainty = outcome == AttemptOutcomes.Unresolved ? "high" : args.Uncertainty ?? "low",
            AssessmentSource = "model",
            RuleVersion = EvidenceRules.RuleVersion,
            Superseded = false,
            Note = args.Note,
            At = at,
        };
        profile.Evidence.Add(record);
        LevelRules.ApplyEvidenceLevelRules(profile, at);
        committed = true;
        return context.Success(null, Data(record, duplicate: false));
    }

    /// <summary>
    /// S96, S121: an explicit <paramref name="taskId"/> binds when that task is active or was completed, ended or abandoned in the
    /// current or previous turn (else <paramref name="found"/> is false → <c>no_active_task</c>). Without one the attempt binds to the
    /// active task, else the most recent task completed or ended (never abandoned) in the current or previous turn, else no task.
    /// </summary>
    private static TaskView? BindTask(ToolContext context, string? taskId, out bool found)
    {
        found = true;
        var turn = context.TurnId;

        bool Recent(TaskView t) => t.ClosedTurnId is { } closed && (closed == turn || closed == turn - 1);

        if (taskId is not null)
        {
            var named = context.Tasks.Find(taskId);
            if (named is not null && (named.Status == TaskStatuses.Active || Recent(named))) return named;
            found = false;
            return null;
        }
        if (context.Tasks.Active is { } active) return active;
        return context.Tasks.History.LastOrDefault(t => (t.Status == TaskStatuses.Completed || t.Status == TaskStatuses.Ended) && Recent(t));
    }

    private static string EngineOutcomeOf(TaskView? task) => task?.Status switch
    {
        null => EngineOutcomes.NoTask,
        TaskStatuses.Completed => EngineOutcomes.TaskCompleted,
        TaskStatuses.Active => EngineOutcomes.TaskActive,
        _ => EngineOutcomeTaskEnded,
    };

    /// <summary>Interaction-sequence position an earlier attempt came from: its source utterance, else its first cited event, else 0.</summary>
    private static long SourcePosition(ToolContext context, EvidenceRecord attempt)
    {
        // Utterance and event ids restart in every engine session: only this session's attempts can be located.
        if (attempt.SessionId != context.SessionId) return 0;
        if (attempt.SourceUtteranceId is not null && context.Utterances.Find(attempt.SourceUtteranceId) is { } utterance) return utterance.Sequence;
        long position = 0;
        foreach (var id in attempt.EventIds)
        {
            var e = context.World.FindEvent(id);
            if (e is not null && (position == 0 || e.Sequence < position)) position = e.Sequence;
        }
        return position;
    }

    private static JsonElement Data(EvidenceRecord record, bool duplicate)
    {
        var node = ToolContext.ToNode(record, SenseiJsonContext.Default.EvidenceRecord)!.AsObject();
        if (duplicate) node["duplicate"] = true;
        return ToolContext.ToData(node);
    }
}

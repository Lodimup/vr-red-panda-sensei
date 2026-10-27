using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Events;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Tasks;
using Sensei.Engine.Trace;
using Sensei.Engine.World;

namespace Sensei.Engine.Tools;

/// <summary>
/// What a tool body sees of the engine for one call. State (<see cref="World"/>, <see cref="Tasks"/>,
/// <see cref="Profile"/>, logs, <see cref="Turn"/>) must be read and changed only under <see cref="Sync"/>;
/// commit through <see cref="Locked"/> so a cancelled or stale call changes nothing (S22, S26, S27, S87).
/// </summary>
public sealed class ToolContext
{
    private readonly EngineCore _core;

    internal ToolContext(EngineCore core, TurnState turn, string callId, string toolName, long generationId, bool isMutating)
    {
        _core = core;
        Turn = turn;
        CallId = callId;
        ToolName = toolName;
        GenerationId = generationId;
        IsMutating = isMutating;
    }

    public string CallId { get; }
    public string ToolName { get; }
    public long GenerationId { get; }
    public bool IsMutating { get; }
    public TurnState Turn { get; }
    public long TurnId => Turn.TurnId;
    public string SessionId => _core.SessionId;

    /// <summary>The engine lock (S9). Never hold it across an await.</summary>
    public object Sync => _core.Sync;

    public SceneWorld World => _core.World;
    public TaskBook Tasks => _core.Tasks;
    public LearnerBook Learner => _core.Learner;

    /// <summary>The live profile (under <see cref="Sync"/>).</summary>
    public LearnerProfile Profile => _core.Learner.Profile;

    public AssistanceLog Assistance => _core.Assistance;
    public UtteranceLog Utterances => _core.Utterances;
    public SpeechHistory SpeechHistory => _core.SpeechHistory;
    public ISpeechOutput Speech => _core.Options.Speech;
    public EngineEventRaiser Events => _core.Events;
    public Secrets Secrets => _core.Secrets;
    public TimeProvider Clock => _core.Clock;

    /// <summary>True once this call's generation is no longer current (S27, S87, S88).</summary>
    public bool IsCancelled => !_core.IsGenerationCurrent(GenerationId);

    /// <summary>Set by <see cref="EndGenerationAfterCall"/>.</summary>
    internal bool EndsGeneration { get; private set; }

    /// <summary>True once a commit made inside this call's <see cref="Locked"/> body completed the active task (S35d).</summary>
    internal bool CompletedTask { get; private set; }

    private bool _committed;
    private long _commitEpoch;

    /// <summary>
    /// True when this mutating call's <see cref="Locked"/> body returned success while its generation was current — it committed —
    /// and no reset has removed the session's effects since (S22, S87). Read under the lock.
    /// </summary>
    internal bool CommitSurvives => _committed && _commitEpoch == _core.ResetEpoch;

    /// <summary>Called by the task book (under the lock) when a Sensei commit completes the task: credits the call whose body is running.</summary>
    internal void NoteTaskCompleted() => CompletedTask = true;

    /// <summary>Next interaction-sequence position (S92). Under <see cref="Sync"/>.</summary>
    public long NextSequence() => _core.NextSequence();

    /// <summary>Next speech utterance id <c>u&lt;n&gt;</c>. Under <see cref="Sync"/>.</summary>
    public string NextSpeechUtteranceId() => _core.NextSpeechUtteranceId();

    /// <summary>
    /// Runs <paramref name="body"/> under the engine lock after re-checking, in order, that the generation is current
    /// (else <c>cancelled</c>) and, for mutating tools, that the scene is not stale (else <c>stale_scene</c>).
    /// The body validates, then commits, and returns the result. A success returned by a mutating tool's body is the commit
    /// (S22): the pipeline keeps it when the generation ends afterwards, and cancels it only when a reset removed it (S87).
    /// </summary>
    public ToolResult Locked(Func<ToolResult> body)
    {
        lock (_core.Sync)
        {
            if (IsCancelled) return Cancelled();
            if (IsMutating)
            {
                var stale = _core.Pipeline.CheckStale(Turn);
                if (stale is not null) return stale;
            }
            var previous = _core.ActiveContext;
            _core.ActiveContext = this;
            try
            {
                var result = body();
                if (IsMutating && result.Status == ToolStatuses.Success)
                {
                    _committed = true;
                    _commitEpoch = _core.ResetEpoch;
                }
                return result;
            }
            finally
            {
                _core.ActiveContext = previous;
            }
        }
    }

    /// <summary>A success result; the pipeline fills <c>call_id</c>, <c>tool</c> and <c>revision</c>.</summary>
    public ToolResult Success(IEnumerable<string>? affectedIds = null, JsonElement? data = null) => new()
    {
        Status = ToolStatuses.Success,
        AffectedIds = affectedIds?.ToList() ?? new List<string>(),
        Data = data,
    };

    public ToolResult Failure(string errorCode, string? detail = null, JsonElement? data = null) => new()
    {
        Status = ToolStatuses.Failure,
        ErrorCode = errorCode,
        Detail = detail,
        Data = data,
    };

    public ToolResult Failure(ToolFailure failure) => Failure(failure.Code, failure.Detail);

    public ToolResult Cancelled() => new()
    {
        Status = ToolStatuses.Cancelled,
        ErrorCode = ToolErrorCodes.Cancelled,
        Detail = "generation cancelled",
    };

    /// <summary>Records one assistance record for a target (SPEC §9). Under <see cref="Sync"/>, only on a committing call.</summary>
    public AssistanceRecord RecordAssistance(string kind, string? dimension, string key, string? taskId)
    {
        var record = new AssistanceRecord
        {
            Kind = kind,
            TargetKey = key,
            TargetDimension = dimension,
            TaskId = taskId,
            TurnId = TurnId,
            EventId = World.LastEventId,
            Sequence = _core.NextSequence(),
        };
        _core.Assistance.Add(record);
        return record;
    }

    /// <summary>Records <paramref name="kind"/> for every skill target of <paramref name="task"/>. Under <see cref="Sync"/>.</summary>
    public void RecordAssistanceForTask(string kind, TaskView task)
    {
        foreach (var t in task.SkillTargets) RecordAssistance(kind, t.Dimension, t.Key, task.TaskId);
    }

    /// <summary>Queues <c>OnSupportChanged</c> with the current settings (raised after the call). Under <see cref="Sync"/>.</summary>
    public void QueueSupportChanged() => _core.QueueSupportChanged();

    /// <summary>Persists the profile (snapshot under the lock, write outside). Call after the committing lock is released.</summary>
    public Task SaveProfileAsync(CancellationToken ct) => _core.Learner.SaveAsync(ct);

    /// <summary>S88: after this call returns success, further calls of this generation are <c>cancelled</c>.</summary>
    public void EndGenerationAfterCall() => EndsGeneration = true;

    /// <summary>A scene snapshot (takes the lock).</summary>
    public SceneSnapshot Snapshot()
    {
        lock (_core.Sync) return World.Snapshot();
    }

    /// <summary>Serializes typed data through the source-generated context.</summary>
    public static JsonElement ToData<T>(T value, JsonTypeInfo<T> typeInfo) => JsonCopy.ToElement(value, typeInfo);

    /// <summary>Serializes a hand-built JSON node (use <see cref="JsonSerializer.SerializeToNode{T}(T, JsonTypeInfo{T})"/> for typed parts).</summary>
    public static JsonElement ToData(JsonNode? node) => JsonCopy.ToElement(node);

    /// <summary>Typed value as a node, for composing <c>data</c> objects.</summary>
    public static JsonNode? ToNode<T>(T value, JsonTypeInfo<T> typeInfo) => JsonSerializer.SerializeToNode(value, typeInfo);
}

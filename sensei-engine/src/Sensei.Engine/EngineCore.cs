using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using Sensei.Engine.World;

namespace Sensei.Engine;

/// <summary>
/// The engine's shared state: one lock (<see cref="Sync"/>, S9) over scene, tasks, learner, logs and turn state.
/// Tools reach it through <see cref="ToolContext"/>; the agent runner receives it directly.
/// </summary>
internal sealed class EngineCore
{
    private readonly List<Action> _unscoped = new();
    private readonly AsyncLocal<NotificationScope?> _scope = new();
    private long _sequence;
    private long _generation;
    private long _turnCounter;
    private long _learnerUtteranceCounter;
    private long _speechUtteranceCounter;
    private CancellationTokenSource _generationCts = new();

    public EngineCore(SenseiEngineOptions options, TimeProvider clock, string sessionId, Secrets secrets, LearnerProfile profile, IEnumerable<SenseiTool>? extraTools)
    {
        Options = options;
        Clock = clock;
        SessionId = sessionId;
        Secrets = secrets;
        World = new SceneWorld(this);
        Tasks = new TaskBook(this);
        Learner = new LearnerBook(Sync, options.ProfileStore, clock, profile);
        Events = new EngineEventRaiser(options.Events, OnEventHandlerError);
        Tools = new ToolRegistry(extraTools);
        Pipeline = new ToolPipeline(this);
        Tools.Attach(Pipeline);
    }

    /// <summary>The one engine lock. Never hold it across an await.</summary>
    public object Sync { get; } = new();

    public SenseiEngineOptions Options { get; }
    public TimeProvider Clock { get; }
    public string SessionId { get; }
    public Secrets Secrets { get; }
    public SceneWorld World { get; }
    public TaskBook Tasks { get; }
    public LearnerBook Learner { get; }
    public AssistanceLog Assistance { get; } = new();
    public UtteranceLog Utterances { get; } = new();
    public SpeechHistory SpeechHistory { get; } = new();
    public EngineEventRaiser Events { get; }
    public ToolRegistry Tools { get; }
    public ToolPipeline Pipeline { get; }

    /// <summary>The current turn, or null before the first <c>BeginTurn</c> (and after a reset).</summary>
    public TurnState? CurrentTurn { get; private set; }

    /// <summary>Turn id of the current turn, 0 before any.</summary>
    public long CurrentTurnId => CurrentTurn?.TurnId ?? 0;

    /// <summary>The live generation; calls made for any other generation are cancelled (S27).</summary>
    public long CurrentGeneration
    {
        get { lock (Sync) return _generation; }
    }

    /// <summary>Cancelled when the current generation is cancelled (S27, S66).</summary>
    public CancellationToken GenerationToken
    {
        get { lock (Sync) return _generationCts.Token; }
    }

    /// <summary>Next position in the engine-wide interaction sequence (S92).</summary>
    public long NextSequence() => ++_sequence;

    /// <summary>Next speech utterance id <c>u&lt;n&gt;</c> (S37d). Call under the lock.</summary>
    public string NextSpeechUtteranceId() => "u" + (++_speechUtteranceCounter).ToString(CultureInfo.InvariantCulture);

    public bool IsGenerationCurrent(long generationId)
    {
        lock (Sync) return generationId == _generation;
    }

    /// <summary>
    /// S23, S92, S117: starts a turn — new turn id, new generation, the transcript recorded as a learner utterance
    /// (synthetic for <c>(cli)</c>), round 1 with the round base at the last scene event, budgets reset.
    /// </summary>
    public TurnState BeginTurnCore(string transcript, bool synthetic, string? correctedFrom = null)
    {
        CancellationTokenSource previousCts;
        TurnState turn;
        lock (Sync)
        {
            _generation++;
            previousCts = _generationCts;
            _generationCts = new CancellationTokenSource();
            var turnId = ++_turnCounter;
            var utterance = new LearnerUtterance
            {
                Id = "l" + (++_learnerUtteranceCounter).ToString(CultureInfo.InvariantCulture),
                TurnId = turnId,
                Text = transcript,
                IsSynthetic = synthetic,
                Sequence = NextSequence(),
                At = Clock.GetUtcNow(),
                SupportAtArrival = Learner.Profile.Support.Clone(),
                CorrectedFrom = correctedFrom,
            };
            Utterances.Add(utterance);
            CurrentTurn = new TurnState
            {
                TurnId = turnId,
                GenerationId = _generation,
                Utterance = utterance,
                Round = 1,
                RoundBaseEventId = World.LastEventId,
            };
            turn = CurrentTurn;
        }
        previousCts.Cancel();
        return turn;
    }

    /// <summary>S26/S84: a new model round — sets the round number and the round base to the last scene event.</summary>
    public void StartRound(int round)
    {
        lock (Sync)
        {
            if (CurrentTurn is null) return;
            CurrentTurn.Round = round;
            CurrentTurn.RoundBaseEventId = World.LastEventId;
        }
    }

    /// <summary><c>MarkRoundStart()</c>: next round number, new round base.</summary>
    public void MarkRoundStart()
    {
        lock (Sync)
        {
            if (CurrentTurn is null) return;
            CurrentTurn.Round++;
            CurrentTurn.RoundBaseEventId = World.LastEventId;
        }
    }

    /// <summary>S27: advances the generation and cancels everything in flight for the old one.</summary>
    public void CancelGeneration()
    {
        CancellationTokenSource old;
        lock (Sync)
        {
            _generation++;
            old = _generationCts;
            _generationCts = new CancellationTokenSource();
        }
        old.Cancel();
    }

    /// <summary>S88: ends <paramref name="generationId"/> for further effects (no-op when it is no longer current).</summary>
    public void EndGeneration(long generationId)
    {
        lock (Sync)
        {
            if (_generation == generationId) _generation++;
        }
    }

    /// <summary>
    /// S87: clears the active task (raising <c>OnTaskChanged</c> with status <c>abandoned</c>), scene objects, assistance log,
    /// utterances, speech history and the idempotency cache. Counters keep increasing. Call under the lock after cancelling the generation.
    /// </summary>
    public void ClearSessionState()
    {
        if (Tasks.Active is { } active)
        {
            active.Status = TaskStatuses.Abandoned;
            active.ClosedTurnId = CurrentTurnId;
            QueueTaskChanged(active);
        }
        Tasks.Clear();
        World.ClearForReset(CurrentTurnId);
        Assistance.Clear();
        Utterances.Clear();
        SpeechHistory.Clear();
        Pipeline.ClearCache();
        CurrentTurn = null;
    }

    /// <summary>Called by scene commits (under the lock): queue the scene notification, then evaluate the active task (S35d).</summary>
    public void AfterCommit(SceneEvent e)
    {
        QueueSceneChanged(e);
        Tasks.Evaluate(e);
    }

    public void QueueSceneChanged(SceneEvent e)
    {
        var copy = e.Clone();
        var snapshot = World.Snapshot();
        Enqueue(() => Events.SceneChanged(copy, snapshot));
    }

    public void QueueTaskChanged(TaskView task)
    {
        var copy = task.Clone();
        Enqueue(() => Events.TaskChanged(copy));
    }

    public void QueueSupportChanged()
    {
        var copy = Learner.Profile.Support.Clone();
        Enqueue(() => Events.SupportChanged(copy));
    }

    /// <summary>
    /// Opens a notification scope for one operation (a tool call, a player action, a turn): notifications its commits queue
    /// are raised only by that operation's <see cref="FlushNotifications"/>, on its own thread (S69). Dispose to close.
    /// </summary>
    public NotificationScope OpenNotificationScope()
    {
        var scope = new NotificationScope(this, _scope.Value);
        _scope.Value = scope;
        return scope;
    }

    /// <summary>Raises the current operation's queued notifications outside the lock, in commit order (S69).</summary>
    public void FlushNotifications()
    {
        List<Action> pending;
        lock (Sync)
        {
            var list = _scope.Value?.Items ?? _unscoped;
            if (list.Count == 0) return;
            pending = new List<Action>(list);
            list.Clear();
        }
        foreach (var raise in pending) raise();
    }

    private void Enqueue(Action raise) => (_scope.Value?.Items ?? _unscoped).Add(raise);

    /// <summary>One operation's pending notifications.</summary>
    internal sealed class NotificationScope : IDisposable
    {
        private readonly EngineCore _core;
        private readonly NotificationScope? _previous;

        public NotificationScope(EngineCore core, NotificationScope? previous)
        {
            _core = core;
            _previous = previous;
        }

        public List<Action> Items { get; } = new();

        public void Dispose() => _core._scope.Value = _previous;
    }
    /// <summary>Writes a sanitized trace entry (S28, S77). Sink exceptions are swallowed: tracing never breaks a call.</summary>
    public void WriteTrace(TraceEntry entry)
    {
        var sink = Options.Trace;
        if (sink is null) return;
        try
        {
            sink.Write(entry);
        }
        catch (Exception)
        {
            // A broken trace sink must not change engine behaviour.
        }
    }

    private void OnEventHandlerError(string method, Exception ex)
    {
        TurnState? turn;
        long revision;
        lock (Sync)
        {
            turn = CurrentTurn;
            revision = World.Revision;
        }
        var entry = new TraceEntry
        {
            At = Clock.GetUtcNow(),
            SessionId = SessionId,
            TurnId = turn?.TurnId ?? 0,
            GenerationId = turn?.GenerationId ?? 0,
            Round = turn?.Round ?? 0,
            CallId = "",
            Tool = "event:" + method,
            Arguments = Secrets.Sanitize(ex.GetType().Name + ": " + ex.Message) ?? "",
            Status = ToolStatuses.Failure,
            ErrorCode = ToolErrorCodes.EventHandlerError,
            Revision = revision,
        };
        WriteTrace(entry);
    }
}

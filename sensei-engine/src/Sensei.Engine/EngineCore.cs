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
    private readonly List<Pending> _unscoped = new();
    private readonly AsyncLocal<NotificationScope?> _scope = new();
    private readonly Queue<TraceEntry> _traceQueue = new();
    private readonly object _sinkGate = new();
    private long _sequence;
    private long _generation;
    private long _callerGeneration;
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

    /// <summary>
    /// The generation calls made without an explicit one belong to: the one the last <c>BeginTurn</c> started (0 before any turn).
    /// It differs from <see cref="CurrentGeneration"/> once the generation was cancelled or ended, even before a first turn exists (S27).
    /// </summary>
    public long CallerGeneration
    {
        get { lock (Sync) return _callerGeneration; }
    }

    /// <summary>The call whose <see cref="ToolContext.Locked"/> body is running; set and read only under <see cref="Sync"/> (S35d).</summary>
    internal ToolContext? ActiveContext { get; set; }

    /// <summary>
    /// Incremented under the lock whenever <see cref="ClearSessionState"/> removes the session's effects (S87). A commit made
    /// under an earlier value no longer exists.
    /// </summary>
    internal long ResetEpoch { get; private set; }

    /// <summary>
    /// S77: the exception boundary of every public <see cref="SenseiEngine"/> member. Returns what the host sees for
    /// <paramref name="ex"/> (see <see cref="ExceptionSanitizer.ForHost"/>) and writes the original, sanitized in full, to the trace.
    /// Never throws; call outside the engine lock.
    /// </summary>
    public Exception Translate(string operation, Exception ex)
    {
        var safe = ExceptionSanitizer.ForHost(ex, Secrets, out var detail, operation, Options.ProfileId);
        if (detail is not null) TraceFailure(operation, detail);
        return safe;
    }

    private void TraceFailure(string operation, string detail, string errorCode = ToolErrorCodes.InternalError, string callId = "")
    {
        try
        {
            lock (Sync) EnqueueTrace(FailureEntry(Clock.GetUtcNow(), SessionId, CurrentTurn, World.Revision, operation, detail, errorCode, callId));
            DrainTraces();
        }
        catch (Exception)
        {
            // The boundary must hand the host its sanitized exception even when tracing the original fails.
        }
    }

    /// <summary>
    /// D4: a profile save that failed after the change it carries was already committed in memory. The change stays; this writes
    /// <c>profile_save_failed</c> to the trace (the exception described by <see cref="ExceptionSanitizer.Describe"/>, so nothing foreign
    /// is formatted) and the learner book keeps the profile dirty until a later write or disposal persists it. Never throws.
    /// </summary>
    public void TraceProfileSaveFailed(Exception ex, string callId = "")
    {
        try
        {
            TraceFailure("profile_save", ExceptionSanitizer.Describe(ex, Secrets), ToolErrorCodes.ProfileSaveFailed, callId);
        }
        catch (Exception)
        {
            // A failure to describe or trace the failure must not turn a committed call into an error.
        }
    }

    /// <summary>
    /// S130: a request to the provider was built without some content of the conversation (a content type outside the allow-list, or a
    /// value over the sanitizing node limit). Writes one <c>engine:outbound_content</c> trace line naming what was removed by type
    /// name, each with its count. Never throws.
    /// </summary>
    public void TraceOutboundContent(IReadOnlyList<string> notes)
    {
        if (notes.Count == 0) return;
        try
        {
            var parts = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var note in notes)
            {
                if (counts.TryGetValue(note, out var seen)) counts[note] = seen + 1;
                else
                {
                    counts[note] = 1;
                    parts.Add(note);
                }
            }
            var detail = string.Join("; ", parts.ConvertAll(p => counts[p] > 1 ? p + " x" + counts[p].ToString(CultureInfo.InvariantCulture) : p));
            TraceFailure("outbound_content", Secrets.Sanitize(detail) ?? "", ToolErrorCodes.OutboundContentRemoved);
        }
        catch (Exception)
        {
            // Describing what was left out of a request must not fail the request.
        }
    }

    /// <summary>
    /// D6: a provider request was not sent because the outbound guard could not check its bytes (<paramref name="reason"/> is one of the
    /// engine's own words, see <c>OutboundRejectedException</c>). Writes one <c>engine:outbound_rejected</c> trace line with error code
    /// <c>outbound_rejected</c>. Never throws.
    /// </summary>
    public void TraceOutboundRejected(string reason)
    {
        try
        {
            TraceFailure("outbound_rejected", Secrets.Sanitize("request not sent: " + reason) ?? "", ToolErrorCodes.OutboundRejected);
        }
        catch (Exception)
        {
            // The turn already fails with provider_error; describing why must not change that.
        }
    }

    /// <summary>The trace line for an engine operation that failed outside a tool call (<paramref name="detail"/> is already sanitized).</summary>
    internal static TraceEntry FailureEntry(DateTimeOffset at, string sessionId, TurnState? turn, long revision, string operation, string detail,
        string errorCode = ToolErrorCodes.InternalError, string callId = "") => new()
    {
        At = at,
        SessionId = sessionId,
        TurnId = turn?.TurnId ?? 0,
        GenerationId = turn?.GenerationId ?? 0,
        Round = turn?.Round ?? 0,
        CallId = callId,
        Tool = "engine:" + operation,
        Arguments = detail,
        Status = ToolStatuses.Failure,
        ErrorCode = errorCode,
        Revision = revision,
    };

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
            _callerGeneration = _generation;
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
        // The epoch moves first: notifications queued before the reset describe effects it removes and are dropped (S87), while the
        // reset's own notifications below belong to the new epoch.
        ResetEpoch++;
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
        _callerGeneration = _generation; // after a reset a call without a turn is no_turn, not cancelled
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

    /// <summary>
    /// Raises the current operation's queued notifications outside the lock, in commit order (S69). A notification queued before a
    /// reset describes an effect the reset removed, so it is dropped (S87); one whose commit survived an ordinary generation
    /// transition is still raised.
    /// </summary>
    public void FlushNotifications()
    {
        List<Action> pending;
        lock (Sync)
        {
            var list = _scope.Value?.Items ?? _unscoped;
            if (list.Count == 0) return;
            pending = new List<Action>(list.Count);
            foreach (var item in list)
                if (item.Epoch == ResetEpoch) pending.Add(item.Raise);
            list.Clear();
        }
        foreach (var raise in pending) raise();
    }

    /// <summary>Queues a notification raised by the current operation's <see cref="FlushNotifications"/>, after its final settlement and outside the lock (S69). Call under the lock, after the change committed.</summary>
    public void QueueNotification(Action raise) => Enqueue(raise);

    private void Enqueue(Action raise) => (_scope.Value?.Items ?? _unscoped).Add(new Pending(ResetEpoch, raise));

    /// <summary>A queued notification and the reset epoch of the state it describes.</summary>
    internal readonly record struct Pending(long Epoch, Action Raise);

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

        public List<Pending> Items { get; } = new();

        public void Dispose() => _core._scope.Value = _previous;
    }
    /// <summary>Queues a trace entry; call under the lock. Entries reach the sink in queue order (S28).</summary>
    public void EnqueueTrace(TraceEntry entry) => _traceQueue.Enqueue(entry);

    /// <summary>
    /// Writes queued trace entries to the sink in the order they were queued, one writer at a time, outside the engine lock (S9).
    /// Never call while holding <see cref="Sync"/>.
    /// </summary>
    public void DrainTraces()
    {
        lock (_sinkGate)
        {
            while (true)
            {
                TraceEntry entry;
                lock (Sync)
                {
                    if (_traceQueue.Count == 0) return;
                    entry = _traceQueue.Dequeue();
                }
                WriteTrace(entry);
            }
        }
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

    /// <summary>
    /// S69: records a handler's exception as <c>event_handler_error</c>. The handler's exception is described by
    /// <see cref="ExceptionSanitizer.Summary"/> (its type name, and its message only for the framework types whose message is a stored
    /// string), and nothing in here can throw: the handler's failure is the host's, and reporting it must not fail the committed call.
    /// </summary>
    private void OnEventHandlerError(string method, Exception ex)
    {
        try
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
                Arguments = ExceptionSanitizer.Summary(ex, Secrets),
                Status = ToolStatuses.Failure,
                ErrorCode = ToolErrorCodes.EventHandlerError,
                Revision = revision,
            };
            lock (Sync) EnqueueTrace(entry);
            DrainTraces();
        }
        catch (Exception)
        {
            // Reporting a handler's failure must never become a second failure.
        }
    }
}

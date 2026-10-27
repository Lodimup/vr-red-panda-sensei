using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Agent;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Storage;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using Sensei.Engine.World;

namespace Sensei.Engine;

/// <summary>
/// The game-master engine (SPEC §3): authoritative scene, tasks, tools, learner model and the agent loop.
/// All public members are thread-safe (S9).
/// </summary>
public sealed class SenseiEngine : IAsyncDisposable
{
    private readonly EngineCore _core;
    private readonly IAgentRunner _runner;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _turnGate = new();
    private Task? _runningTurn;
    private long _turnTicket;
    private bool _previousTurnInterrupted;
    private bool _disposed;

    private SenseiEngine(EngineCore core, Func<EngineCore, IAgentRunner>? runnerFactory)
    {
        _core = core;
        _runner = runnerFactory?.Invoke(core) ?? new SenseiAgentRunner(core);
    }

    /// <summary>S7: loads or creates the profile, appends this session's record and saves.</summary>
    public static Task<SenseiEngine> CreateAsync(SenseiEngineOptions options, CancellationToken ct = default) =>
        CreateAsync(options, null, null, ct);

    /// <summary>Test/host seam: extra (non-standard) tools and a custom agent runner.</summary>
    internal static async Task<SenseiEngine> CreateAsync(SenseiEngineOptions options, IEnumerable<SenseiTool>? extraTools,
        Func<EngineCore, IAgentRunner>? runnerFactory, CancellationToken ct = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.Speech is null) throw new ArgumentException("Speech is required", nameof(options));
        if (options.ProfileStore is null) throw new ArgumentException("ProfileStore is required", nameof(options));
        ProfileIds.Validate(options.ProfileId, nameof(options));

        var clock = options.Clock ?? TimeProvider.System;
        var now = clock.GetUtcNow();
        var sessionId = NewSessionId(now);
        var profile = await options.ProfileStore.LoadAsync(options.ProfileId, ct).ConfigureAwait(false)
                      ?? LearnerProfile.CreateDefault(options.ProfileId, now);
        var problem = profile.ProfileId != options.ProfileId ? "profile_id" : profile.FindShapeProblem();
        if (problem is not null)
            throw new ProfileCorruptException(options.ProfileId, options.ProfileId + ".json", new FormatException("invalid field: " + problem));
        profile.Sessions.Add(new SessionRecord { SessionId = sessionId, StartedAt = now });

        var core = new EngineCore(options, clock, sessionId, new Secrets(options.KnownSecrets), profile, extraTools);
        await core.Learner.SaveAsync(ct).ConfigureAwait(false);
        return new SenseiEngine(core, runnerFactory);
    }

    internal EngineCore Core => _core;

    /// <summary><c>s-&lt;yyyyMMddHHmmss&gt;-&lt;4 lowercase hex&gt;</c> (S7).</summary>
    public string SessionId => _core.SessionId;

    /// <summary>The 16 tool names in S10 order.</summary>
    public IReadOnlyList<string> ToolNames => _core.Tools.Names;

    public SceneSnapshot GetScene()
    {
        lock (_core.Sync) return _core.World.Snapshot();
    }

    /// <summary>Deep copy, safe to keep.</summary>
    public LearnerProfileView GetProfile()
    {
        lock (_core.Sync) return LearnerBook.ToView(_core.Learner.Profile);
    }

    /// <summary>The task whose status is <c>active</c>, or null (S96).</summary>
    public TaskView? GetActiveTask()
    {
        lock (_core.Sync) return _core.Tasks.Active?.Clone();
    }

    // ---------------------------------------------------------------- conversation

    /// <summary>S23, S68, S117: cancels a running turn and waits for it, starts a turn (utterance recorded first) and runs the agent.</summary>
    public Task<TurnResult> RunTurnAsync(string transcript, CancellationToken ct = default) =>
        RunGatedAsync(() => Task.FromResult(StartTurn(transcript ?? "", null)), ct);

    /// <summary>S48, S99, S116: supersedes the latest learner utterance and its attempts, then runs a normal turn with the correction.</summary>
    public Task<TurnResult> CorrectLastTranscriptAsync(string correctedTranscript, CancellationToken ct = default) =>
        RunGatedAsync(() => StartCorrectionAsync(correctedTranscript ?? ""), ct);

    /// <summary>S27: advances the generation; in-flight calls and speech of the old one end <c>cancelled</c>.</summary>
    public void CancelGeneration() => _core.CancelGeneration();

    // ---------------------------------------------------------------- manual turns

    /// <summary>S23: starts a manual turn. Throws <see cref="InvalidOperationException"/> (<c>turn_in_progress</c>) while a turn runs (S87).</summary>
    public void BeginTurn(string transcript)
    {
        if (!_lifecycle.Wait(0)) throw new InvalidOperationException("turn_in_progress: a turn is running");
        try
        {
            var text = transcript ?? "";
            _core.BeginTurnCore(text, synthetic: text == "(cli)");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>S26: starts a new round within the current turn (round base = last scene event).</summary>
    public void MarkRoundStart() => _core.MarkRoundStart();

    /// <summary>Invokes a tool as Sensei through the same pipeline the agent uses.</summary>
    public Task<ToolResult> InvokeToolAsync(string toolName, string argumentsJson, string? callId = null, CancellationToken ct = default) =>
        _core.Pipeline.InvokeAsync(toolName, argumentsJson, callId, null, ct);

    // ---------------------------------------------------------------- player actions

    /// <summary>S18, S105: selects an object (no revision change) and evaluates the active task.</summary>
    public PlayerActionResult PlayerSelect(string instanceId)
    {
        using var scope = _core.OpenNotificationScope();
        PlayerActionResult result;
        lock (_core.Sync)
        {
            if (!_core.World.Exists(instanceId))
            {
                result = new PlayerActionResult { Ok = false, ErrorCode = ToolErrorCodes.UnknownObject, Detail = $"unknown object '{instanceId}'", Revision = _core.World.Revision };
            }
            else
            {
                _core.World.CommitSelect(instanceId, SceneActors.Player, _core.CurrentTurnId);
                result = new PlayerActionResult { Ok = true, Revision = _core.World.Revision };
            }
        }
        _core.FlushNotifications();
        return result;
    }

    /// <summary>S19, S115: moves an object as the player under the same destination rules as <c>move_object</c>.</summary>
    public PlayerActionResult PlayerMove(string instanceId, string? region, string? containerId)
    {
        using var scope = _core.OpenNotificationScope();
        PlayerActionResult result;
        lock (_core.Sync)
        {
            var failure = _core.World.ValidateDestination(instanceId, region, containerId);
            if (failure is not null)
            {
                result = new PlayerActionResult { Ok = false, ErrorCode = failure.Code, Detail = failure.Detail, Revision = _core.World.Revision };
            }
            else
            {
                _core.World.CommitMove(instanceId, region, containerId, SceneActors.Player, null, _core.CurrentTurnId);
                result = new PlayerActionResult { Ok = true, Revision = _core.World.Revision };
            }
        }
        _core.FlushNotifications();
        return result;
    }

    /// <summary>S40, S76: sets the given support fields and pins them; saves. Invalid values → <see cref="ArgumentException"/>.</summary>
    public async Task SetPreferenceByPlayerAsync(SupportPatch patch, CancellationToken ct = default)
    {
        if (patch is null) throw new ArgumentNullException(nameof(patch));
        var invalid = patch.Validate();
        if (invalid is not null) throw new ArgumentException(invalid, nameof(patch));
        using var scope = _core.OpenNotificationScope();
        lock (_core.Sync)
        {
            SupportRules.ApplyPlayerPatch(_core.Learner.Profile, _core.Learner.CurrentSession(_core.SessionId), patch);
            _core.QueueSupportChanged();
        }
        try
        {
            await _core.Learner.SaveAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _core.FlushNotifications();
        }
    }

    /// <summary>S42: like <c>set_support</c> with <c>starting_band</c> and reason <c>learner_request</c>, as the player.</summary>
    public async Task SetStartingBandAsync(string band, CancellationToken ct = default)
    {
        if (!StartingBands.IsBand(band))
            throw new ArgumentException($"starting band must be one of {string.Join(", ", StartingBands.All)}", nameof(band));
        using var scope = _core.OpenNotificationScope();
        lock (_core.Sync)
        {
            SupportRules.ApplyStartingBand(_core.Learner.Profile, _core.Learner.CurrentSession(_core.SessionId), band, PreferenceActors.Player, _core.Clock.GetUtcNow());
            _core.QueueSupportChanged();
        }
        try
        {
            await _core.Learner.SaveAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _core.FlushNotifications();
        }
    }

    /// <summary>Read-only summary of this session (S55 shape); ends nothing.</summary>
    public SessionSummary GetSessionSummary()
    {
        lock (_core.Sync) return SessionSummaryFactory.Build(_core.Learner.Profile, _core.SessionId);
    }

    /// <summary>
    /// S60, S87: cancels and awaits the running turn, deletes the stored profile and replaces it with a new default one,
    /// removes every scene object and clears task, assistance, utterances, speech history, cache and conversation.
    /// </summary>
    public async Task ResetProfileAsync(CancellationToken ct = default)
    {
        using var scope = _core.OpenNotificationScope();
        lock (_turnGate) _turnTicket++; // queued turns are superseded by the reset
        await CancelRunningTurnAsync().ConfigureAwait(false);
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _core.CancelGeneration();
            await _core.Learner.DeleteAsync(_core.Options.ProfileId, ct).ConfigureAwait(false);
            lock (_core.Sync)
            {
                var now = _core.Clock.GetUtcNow();
                var profile = LearnerProfile.CreateDefault(_core.Options.ProfileId, now);
                profile.Sessions.Add(new SessionRecord { SessionId = _core.SessionId, StartedAt = now });
                _core.Learner.Profile = profile;
                _core.ClearSessionState();
                _core.QueueSupportChanged();
            }
            _runner.ResetConversation();
            _previousTurnInterrupted = false;
            await _core.Learner.SaveAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
        _core.FlushNotifications();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await CancelRunningTurnAsync().ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- internals

    private async Task<TurnResult> RunGatedAsync(Func<Task<Prepared>> start, CancellationToken ct)
    {
        using var scope = _core.OpenNotificationScope();
        long ticket;
        lock (_turnGate) ticket = ++_turnTicket;
        await CancelRunningTurnAsync().ConfigureAwait(false);
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        var settled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool superseded;
        lock (_turnGate)
        {
            superseded = ticket != _turnTicket;
            if (!superseded) _runningTurn = settled.Task;
        }
        if (superseded)
        {
            // S68: a newer turn (or a reset) arrived while this one waited; it never starts.
            _lifecycle.Release();
            return new TurnResult { TurnId = _core.CurrentTurnId, GenerationId = _core.CurrentGeneration, Status = TurnStatuses.Cancelled };
        }
        TurnResult result;
        try
        {
            var prepared = await start().ConfigureAwait(false);
            bool supersededAfterStart;
            lock (_turnGate) supersededAfterStart = ticket != _turnTicket;
            if (prepared.Immediate is not null)
            {
                result = prepared.Immediate;
            }
            else if (supersededAfterStart)
            {
                // A newer request arrived between the gate and the turn start: its cancel may have hit the old generation.
                _core.CancelGeneration();
                result = new TurnResult { TurnId = prepared.Request!.Turn.TurnId, GenerationId = prepared.Request.Turn.GenerationId, Status = TurnStatuses.Cancelled };
            }
            else if (_core.Options.ChatClient is null)
            {
                result = Failed(prepared.Request!.Turn, TurnErrorCodes.LlmNotConfigured, "no chat client is configured");
            }
            else
            {
                try
                {
                    result = await _runner.RunTurnAsync(prepared.Request!, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result = new TurnResult
                    {
                        TurnId = prepared.Request!.Turn.TurnId,
                        GenerationId = prepared.Request.Turn.GenerationId,
                        Status = TurnStatuses.Cancelled,
                    };
                }
            }
            if (prepared.Request is not null)
            {
                lock (_core.Sync) result.ToolCalls = new List<TraceEntry>(prepared.Request.Turn.TraceEntries);
                _previousTurnInterrupted = result.Status == TurnStatuses.Cancelled;
            }
            result.ErrorDetail = _core.Secrets.Sanitize(result.ErrorDetail);
        }
        finally
        {
            lock (_turnGate) _runningTurn = null;
            _lifecycle.Release();
            settled.TrySetResult(true);
        }
        _core.FlushNotifications();
        _core.Events.SenseiState(SenseiStates.Idle);
        return result;
    }

    private Prepared StartTurn(string transcript, string? correctedFrom)
    {
        var turn = _core.BeginTurnCore(transcript, synthetic: false, correctedFrom);
        return new Prepared { Request = new AgentTurnRequest(turn, transcript, correctedFrom, _previousTurnInterrupted) };
    }

    /// <summary>S99: supersede the latest non-synthetic utterance and every attempt sourced from it, save, then start the turn.</summary>
    private async Task<Prepared> StartCorrectionAsync(string correctedTranscript)
    {
        string previousText;
        lock (_core.Sync)
        {
            var target = _core.Utterances.LatestCorrectable();
            if (target is null)
            {
                return new Prepared
                {
                    Immediate = new TurnResult
                    {
                        TurnId = _core.CurrentTurnId,
                        GenerationId = _core.CurrentGeneration,
                        Status = TurnStatuses.Failed,
                        ErrorCode = TurnErrorCodes.NothingToCorrect,
                        ErrorDetail = "there is no learner transcript to correct",
                    },
                };
            }
            target.Superseded = true;
            previousText = target.Text;
            var profile = _core.Learner.Profile;
            foreach (var attempt in profile.Evidence)
                if (attempt.SourceUtteranceId == target.Id) attempt.Superseded = true;
            LevelRules.RecomputeAfterSupersede(profile, _core.Clock.GetUtcNow());
        }
        await _core.Learner.SaveAsync(CancellationToken.None).ConfigureAwait(false);
        return StartTurn(correctedTranscript, previousText);
    }

    private async Task CancelRunningTurnAsync()
    {
        Task? running;
        lock (_turnGate) running = _runningTurn;
        if (running is null) return;
        _core.CancelGeneration();
        await running.ConfigureAwait(false);
    }

    private static TurnResult Failed(TurnState turn, string code, string detail) => new()
    {
        TurnId = turn.TurnId,
        GenerationId = turn.GenerationId,
        Status = TurnStatuses.Failed,
        ErrorCode = code,
        ErrorDetail = detail,
    };

    private static string NewSessionId(DateTimeOffset now)
    {
        var bytes = new byte[2];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return "s-" + now.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + bytes[0].ToString("x2", CultureInfo.InvariantCulture) + bytes[1].ToString("x2", CultureInfo.InvariantCulture);
    }

    private sealed class Prepared
    {
        public TurnResult? Immediate { get; set; }
        public AgentTurnRequest? Request { get; set; }
    }
}

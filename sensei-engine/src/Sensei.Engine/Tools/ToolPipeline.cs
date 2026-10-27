using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Tools.Schema;
using Sensei.Engine.Trace;
using Sensei.Engine.World;

namespace Sensei.Engine.Tools;

/// <summary>
/// The one path every tool call takes, from the agent loop or <c>InvokeToolAsync</c> (SPEC §6).
/// Checks run in S81 order: generation cancelled → no turn → cache → turn exhausted → unknown tool →
/// JSON syntax / duplicate names / schema → same-round dependency (S84) → stale scene (S26) → the tool's own checks.
/// Every call writes one trace entry (S28, S86).
/// </summary>
internal sealed class ToolPipeline
{
    private const string AutoPrefix = "auto-";

    private readonly EngineCore _core;
    private readonly Dictionary<CacheKey, CacheEntry> _cache = new();
    private readonly Dictionary<CacheKey, InFlightCall> _inFlight = new();
    private long _autoCounter;

    public ToolPipeline(EngineCore core)
    {
        _core = core;
    }

    /// <summary>
    /// Invokes <paramref name="toolName"/> as Sensei. <paramref name="generationId"/> is the generation the caller works for
    /// (the agent loop passes its turn's); null means the current turn's generation.
    /// </summary>
    public async Task<ToolResult> InvokeAsync(string toolName, string? argumentsJson, string? callId, long? generationId, CancellationToken ct)
    {
        using var scope = _core.OpenNotificationScope();
        var stopwatch = Stopwatch.StartNew();
        Call call;
        lock (_core.Sync)
        {
            call = Precheck(toolName ?? "", argumentsJson, callId, generationId, stopwatch);
        }

        // S24: the same call id while the first call is still running waits for it and returns its result.
        if (call.Pending is not null)
        {
            var first = await call.Pending.ConfigureAwait(false);
            lock (_core.Sync) WriteTrace(call, first, stopwatch, duplicate: true);
            WritePendingTraces(call);
            return first;
        }

        if (call.Done is not null)
        {
            WritePendingTraces(call);
            _core.FlushNotifications();
            return call.Done;
        }

        ToolResult result;
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, call.GenerationToken))
        {
            try
            {
                result = await call.Tool!.ExecuteBoxedAsync(call.Arguments!, call.Context!, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                result = call.Context!.Cancelled();
            }
            catch (Exception ex)
            {
                lock (_core.Sync)
                {
                    var failed = call.Context!.Failure(ToolErrorCodes.InternalError, ex.GetType().Name);
                    Finish(call, failed, stopwatch, cache: false, countFailure: false);
                    if (call.InFlight is not null)
                    {
                        _inFlight.Remove(call.InFlight.Key);
                        call.InFlight.Completion.TrySetException(ex);
                    }
                }
                WritePendingTraces(call);
                _core.FlushNotifications();
                throw;
            }
        }

        lock (_core.Sync)
        {
            if (result.Status == ToolStatuses.Success && CompletedBySenseiSince(call.EventMark))
                result.Data = WithTaskStatus(result.Data);
            if (result.Status == ToolStatuses.Success && call.Context!.EndsGeneration)
                _core.EndGeneration(call.GenerationId);
            result = Finish(call, result, stopwatch, cache: true, countFailure: true);
        }
        WritePendingTraces(call);
        _core.FlushNotifications();
        return result;
    }


    /// <summary>S26: <c>stale_scene</c> (with a fresh snapshot) when a player <c>moved</c> event is newer than the round base. Under the lock.</summary>
    public ToolResult? CheckStale(TurnState turn)
    {
        var playerMoved = _core.World.EventsAfter(turn.RoundBaseEventId)
            .Any(e => e.Kind == SceneEventKinds.Moved && e.Actor == SceneActors.Player);
        if (!playerMoved) return null;
        return new ToolResult
        {
            Status = ToolStatuses.Failure,
            ErrorCode = ToolErrorCodes.StaleScene,
            Detail = "the learner moved something since this round began; inspect the new scene",
            Data = JsonCopy.ToElement(_core.World.Snapshot(), SenseiJsonContext.Default.SceneSnapshot),
        };
    }

    /// <summary>S87: forget every cached result.</summary>
    public void ClearCache()
    {
        _cache.Clear();
        _inFlight.Clear();
    }

    private Call Precheck(string toolName, string? argumentsJson, string? callId, long? generationId, Stopwatch stopwatch)
    {
        var turn = _core.CurrentTurn;
        var current = _core.CurrentGeneration;
        var generation = generationId ?? turn?.GenerationId ?? current;
        var call = new Call
        {
            ToolName = toolName,
            ArgumentsJson = argumentsJson ?? "",
            Turn = turn,
            GenerationId = generation,
            EventMark = _core.World.LastEventId,
        };

        // 1. generation cancelled (S27, S82, S87)
        if ((generationId.HasValue && generationId.Value != current) || (turn is not null && turn.GenerationId != current))
        {
            call.CallId = callId ?? NextAutoId();
            return Done(call, Cancelled(), stopwatch, cache: false, countFailure: false);
        }

        // 2. no turn (S23)
        if (turn is null)
        {
            call.CallId = callId ?? NextAutoId();
            return Done(call, Fail(ToolErrorCodes.NoTurn, "no current turn: call BeginTurn first"), stopwatch, cache: false, countFailure: false);
        }

        var reserved = callId is not null && callId.StartsWith(AutoPrefix, StringComparison.Ordinal);
        call.CallId = callId ?? NextAutoId();
        var key = new CacheKey(turn.TurnId, generation, call.CallId);
        var reused = false;

        // 3. cache (S24, S82)
        if (!reserved && _cache.TryGetValue(key, out var cached))
        {
            if (cached.Tool == toolName && cached.CanonicalArguments == Canonical(argumentsJson))
            {
                WriteTrace(call, cached.Result, stopwatch, duplicate: true);
                call.Done = cached.Result;
                return call;
            }
            reused = true;
        }
        else if (!reserved && _inFlight.TryGetValue(key, out var running))
        {
            if (running.Tool == toolName && running.CanonicalArguments == Canonical(argumentsJson))
            {
                call.Pending = running.Completion.Task;
                return call;
            }
            reused = true;
        }
        var cacheThis = !reserved && !reused;

        // 4. turn exhausted (S25)
        if (turn.Exhausted)
            return Done(call, Fail(ToolErrorCodes.TurnBudgetExhausted, "no more tool calls this turn"), stopwatch, cacheThis, countFailure: false);
        turn.CallsCounted++;
        if (turn.CallsCounted > TurnState.CallBudget)
        {
            turn.Exhausted = true;
            return Done(call, Fail(ToolErrorCodes.TurnBudgetExhausted, $"at most {TurnState.CallBudget} tool calls per turn"), stopwatch, cacheThis, countFailure: false);
        }

        if (reserved) return Done(call, Fail(ToolErrorCodes.InvalidArguments, ToolDetails.ReservedCallId), stopwatch, cacheThis, countFailure: true);
        if (reused) return Done(call, Fail(ToolErrorCodes.InvalidArguments, ToolDetails.CallIdReused), stopwatch, cacheThis, countFailure: true);

        // 5. unknown tool
        var tool = _core.Tools.Find(toolName);
        if (tool is null)
            return Done(call, Fail(ToolErrorCodes.UnknownTool, $"unknown tool '{toolName}'"), stopwatch, cacheThis, countFailure: true);
        call.Tool = tool;

        // 6. JSON syntax, duplicate property names, schema, typed arguments (S21, S83, S114)
        if (!ArgumentSchema.TryParse(argumentsJson, out var element))
            return Done(call, Fail(ToolErrorCodes.InvalidArguments, ToolDetails.MalformedJson), stopwatch, cacheThis, countFailure: true);
        var schemaError = tool.Schema.Validate(element);
        if (schemaError is not null)
            return Done(call, Fail(ToolErrorCodes.InvalidArguments, schemaError), stopwatch, cacheThis, countFailure: true);
        var prepared = tool.Prepare(tool.Schema.RemoveNullOptionals(element));
        if (prepared.InvalidDetail is not null)
            return Done(call, Fail(ToolErrorCodes.InvalidArguments, prepared.InvalidDetail), stopwatch, cacheThis, countFailure: true);

        // 7. same-round dependency (S84)
        foreach (var id in prepared.ReferencedIds)
        {
            var spawned = _core.World.SpawnEventIdOf(id);
            if (spawned is not null && spawned.Value > turn.RoundBaseEventId)
                return Done(call, Fail(ToolErrorCodes.InvalidArguments, ToolDetails.IdNotYetVisible), stopwatch, cacheThis, countFailure: true);
        }

        // 8. stale scene (S26)
        if (tool.IsMutating)
        {
            var stale = CheckStale(turn);
            if (stale is not null) return Done(call, stale, stopwatch, cacheThis, countFailure: true);
        }

        call.Arguments = prepared.Value;
        call.CacheThis = cacheThis;
        call.GenerationToken = _core.GenerationToken;
        call.Context = new ToolContext(_core, turn, call.CallId, toolName, generation, tool.IsMutating);
        if (cacheThis)
        {
            call.InFlight = new InFlightCall(key, toolName, Canonical(argumentsJson), new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously));
            _inFlight[key] = call.InFlight;
        }
        return call;
    }

    private Call Done(Call call, ToolResult result, Stopwatch stopwatch, bool cache, bool countFailure)
    {
        call.CacheThis = cache;
        call.Done = Finish(call, result, stopwatch, cache, countFailure);
        return call;
    }

    /// <summary>Fills identity and revision, counts validation failures, sanitizes, caches and traces. Under the lock.</summary>
    private ToolResult Finish(Call call, ToolResult result, Stopwatch stopwatch, bool cache, bool countFailure)
    {
        result.CallId = call.CallId;
        result.Tool = call.ToolName;
        result.Revision = _core.World.Revision;
        result.AffectedIds ??= new List<string>();

        var turn = call.Turn;
        if (countFailure && turn is not null && result.Status == ToolStatuses.Failure && ToolErrorCodes.CountsAsValidationFailure(result.ErrorCode))
        {
            turn.ValidationFailures++;
            if (turn.ValidationFailures >= TurnState.ValidationFailureLimit) turn.Exhausted = true;
        }

        var sanitized = Sanitize(result);
        if (cache && call.CacheThis && turn is not null)
            _cache[new CacheKey(turn.TurnId, call.GenerationId, call.CallId)] = new CacheEntry(call.ToolName, Canonical(call.ArgumentsJson), sanitized);
        if (call.InFlight is not null && cache)
        {
            _inFlight.Remove(call.InFlight.Key);
            call.InFlight.Completion.TrySetResult(sanitized);
        }
        WriteTrace(call, sanitized, stopwatch, duplicate: false);
        return sanitized;
    }

    private void WriteTrace(Call call, ToolResult result, Stopwatch stopwatch, bool duplicate)
    {
        var secrets = _core.Secrets;
        var entry = new TraceEntry
        {
            At = _core.Clock.GetUtcNow(),
            SessionId = _core.SessionId,
            TurnId = call.Turn?.TurnId ?? 0,
            GenerationId = call.GenerationId,
            Round = call.Turn?.Round ?? 0,
            CallId = secrets.Sanitize(call.CallId) ?? "",
            Tool = secrets.Sanitize(call.ToolName) ?? "",
            Arguments = SanitizeJsonText(call.ArgumentsJson),
            Status = result.Status,
            ErrorCode = result.ErrorCode,
            Revision = result.Revision,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            Duplicate = duplicate,
            Result = result,
        };
        call.Turn?.TraceEntries.Add(entry);
        call.PendingTraces.Add(entry);
    }

    /// <summary>Hands queued trace entries to the sink outside the engine lock.</summary>
    private void WritePendingTraces(Call call)
    {
        List<TraceEntry> entries;
        lock (_core.Sync)
        {
            entries = new List<TraceEntry>(call.PendingTraces);
            call.PendingTraces.Clear();
        }
        foreach (var e in entries) _core.WriteTrace(e);
    }

    /// <summary>S77: a copy of <paramref name="result"/> with every string sanitized.</summary>
    private ToolResult Sanitize(ToolResult result)
    {
        var s = _core.Secrets;
        return new ToolResult
        {
            CallId = s.Sanitize(result.CallId) ?? "",
            Tool = s.Sanitize(result.Tool) ?? "",
            Status = result.Status,
            ErrorCode = result.ErrorCode,
            Detail = s.Sanitize(result.Detail),
            Revision = result.Revision,
            AffectedIds = result.AffectedIds.Select(id => s.Sanitize(id) ?? "").ToList(),
            Data = result.Data is { } d ? SanitizeElement(d) : null,
        };
    }

    private JsonElement SanitizeElement(JsonElement element)
    {
        var text = SanitizedJson(element);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>Sanitized arguments text: values and names cleaned when it parses (duplicates kept), else the raw text redacted.</summary>
    private string SanitizeJsonText(string json)
    {
        if (!string.IsNullOrWhiteSpace(json) && ArgumentSchema.TryParse(json, out var element)) return SanitizedJson(element);
        return _core.Secrets.Sanitize(json) ?? "";
    }

    /// <summary>Compact JSON of <paramref name="element"/> with every string value and property name sanitized (S77).</summary>
    private string SanitizedJson(JsonElement element)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteSanitized(element, writer);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteSanitized(JsonElement element, Utf8JsonWriter writer)
    {
        var s = _core.Secrets;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in element.EnumerateObject())
                {
                    writer.WritePropertyName(s.Sanitize(p.Name) ?? p.Name);
                    WriteSanitized(p.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteSanitized(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(s.Sanitize(element.GetString()) ?? "");
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private bool CompletedBySenseiSince(long eventMark) =>
        _core.World.EventsAfter(eventMark).Any(e => e.Kind == SceneEventKinds.TaskCompleted && e.Actor == SceneActors.Sensei);

    private static JsonElement WithTaskStatus(JsonElement? data)
    {
        JsonObject obj;
        if (data is { ValueKind: JsonValueKind.Object } d) obj = (JsonObject)JsonNode.Parse(d.GetRawText())!;
        else obj = new JsonObject();
        obj["task_status"] = "completed";
        return JsonCopy.ToElement(obj);
    }

    private static string Canonical(string? json) => CanonicalJson.TryCanonicalize(json) ?? ("raw:" + json);

    private string NextAutoId() => AutoPrefix + (++_autoCounter).ToString(CultureInfo.InvariantCulture);

    private static ToolResult Fail(string code, string detail) => new() { Status = ToolStatuses.Failure, ErrorCode = code, Detail = detail };

    private static ToolResult Cancelled() => new() { Status = ToolStatuses.Cancelled, ErrorCode = ToolErrorCodes.Cancelled, Detail = "generation cancelled" };

    private readonly record struct CacheKey(long TurnId, long GenerationId, string CallId);

    private sealed record CacheEntry(string Tool, string CanonicalArguments, ToolResult Result);

    private sealed record InFlightCall(CacheKey Key, string Tool, string CanonicalArguments, TaskCompletionSource<ToolResult> Completion);

    private sealed class Call
    {
        public string ToolName = "";
        public string ArgumentsJson = "";
        public string CallId = "";
        public TurnState? Turn;
        public long GenerationId;
        public long EventMark;
        public bool CacheThis;
        public SenseiTool? Tool;
        public object? Arguments;
        public ToolContext? Context;
        public CancellationToken GenerationToken;
        public ToolResult? Done;
        public Task<ToolResult>? Pending;
        public InFlightCall? InFlight;
        public List<TraceEntry> PendingTraces = new();
    }
}

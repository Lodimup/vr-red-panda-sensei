using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Sensei.Engine.Events;
using Sensei.Engine.Json;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Agent;

/// <summary>
/// The function-invocation middleware (S61, S85): every provider function call — known tool, unknown name or arguments
/// that failed to parse — goes through <see cref="ToolPipeline"/>, which counts, validates, executes and traces it, and the
/// engine's <c>ToolResult</c> is what the model gets back. The framework never answers a call itself.
/// </summary>
internal sealed class EngineFunctionInvoker
{
    /// <summary>Stands in for arguments the provider could not parse; deliberately not valid JSON (it is also what the trace shows).</summary>
    internal const string UnparsedArguments = "<unparsed arguments>";

    private readonly EngineCore _core;
    private readonly Func<TurnScope?> _active;
    private readonly Action<TurnScope, string> _raiseState;
    private readonly HashSet<string> _unavailable;

    public EngineFunctionInvoker(EngineCore core, Func<TurnScope?> active, Action<TurnScope, string> raiseState, HashSet<string>? unavailable = null)
    {
        _core = core;
        _active = active;
        _raiseState = raiseState;
        _unavailable = unavailable ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public async ValueTask<object?> InvokeAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        var scope = _active() ?? throw new InvalidOperationException("a function was invoked outside an agent turn");
        var call = context.CallContent;
        var name = call.Name ?? "";
        var json = ArgumentsText(call);
        var callId = string.IsNullOrEmpty(call.CallId) ? null : call.CallId;

        if (SpeaksAloud(name, json)) _raiseState(scope, SenseiStates.Speaking);

        ToolResult result;
        try
        {
            result = await _core.Pipeline.InvokeAsync(name, json, callId, scope.Turn.GenerationId, cancellationToken, _unavailable.Contains(name)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The pipeline already traced the failure (S28). The model still gets exactly one engine result for this call id.
            result = new ToolResult
            {
                CallId = _core.Secrets.Sanitize(call.CallId) ?? "",
                Tool = _core.Secrets.Sanitize(name) ?? "",
                Status = ToolStatuses.Failure,
                ErrorCode = ToolErrorCodes.InternalError,
                Detail = ex.GetType().Name,
                Revision = RevisionNow(),
            };
        }
        return JsonSerializer.SerializeToElement(result, SenseiJsonContext.Default.ToolResult);
    }

    /// <summary>
    /// The arguments exactly as the model sent them when the provider kept them (so duplicate names survive, S83);
    /// otherwise rebuilt from the parsed dictionary. When the provider could not parse them and kept no raw text, a marker that
    /// is not JSON, so the pipeline answers <c>invalid_arguments</c> (S21, S85) instead of treating the call as having no arguments.
    /// </summary>
    internal static string ArgumentsText(FunctionCallContent call)
    {
        if (call.RawRepresentation is ChatToolCall raw)
        {
            var text = raw.FunctionArguments?.ToString();
            if (text is not null) return text;
        }
        if (call.Exception is not null) return UnparsedArguments;
        IEnumerable<KeyValuePair<string, object?>> arguments = call.Arguments is { } parsed ? parsed : Array.Empty<KeyValuePair<string, object?>>();
        return SenseiTool.ArgumentsToJson(arguments);
    }

    private static bool SpeaksAloud(string tool, string argumentsJson)
    {
        if (tool == "speak" || tool == "replay_speech") return true;
        if (tool != "show_hint") return false;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson, new JsonDocumentOptions { MaxDepth = JsonRedaction.WalkDepth(JsonRedaction.MaxFinalValues) });
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("speak", out var speak)
                   && speak.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private long RevisionNow()
    {
        lock (_core.Sync) return _core.World.Revision;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Sensei.Engine.Events;

namespace Sensei.Engine.Agent;

/// <summary>
/// The delegating chat client under function invocation (S64, S63, S85): counts the model requests of the turn and
/// refuses the 5th (an empty assistant message ends the loop), starts a model round in the engine before each request
/// (S26), refreshes the engine-owned context and the learner block, adds usage, collects plain assistant text, and
/// tells the function-invocation loop about tool names it does not know so the engine pipeline answers them (S85).
/// </summary>
internal sealed class RoundLimitingChatClient : DelegatingChatClient
{
    private readonly EngineCore _core;
    private readonly ContextRenderer _renderer;
    private readonly Func<TurnScope?> _active;
    private readonly IList<AITool> _dynamicTools;
    private readonly HashSet<string> _offered;
    private readonly Action<TurnScope, string> _raiseState;
    private readonly bool _resultsAsText;

    public RoundLimitingChatClient(IChatClient inner, EngineCore core, ContextRenderer renderer, Func<TurnScope?> active,
        IList<AITool> dynamicTools, IEnumerable<string> offeredToolNames, Action<TurnScope, string> raiseState, bool resultsAsText = false)
        : base(inner)
    {
        _core = core;
        _renderer = renderer;
        _active = active;
        _dynamicTools = dynamicTools;
        _offered = new HashSet<string>(offeredToolNames, StringComparer.Ordinal);
        _raiseState = raiseState;
        _resultsAsText = resultsAsText;
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var scope = _active();
        if (scope is null) return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (scope.Rounds >= ContextRenderer.MaxRounds)
        {
            // S64: the 5th request never reaches the provider; an empty assistant message ends the loop.
            scope.BudgetExhausted = true;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, ""));
        }

        scope.Rounds++;
        _core.StartRound(scope.Rounds);
        _raiseState(scope, SenseiStates.Thinking);

        var (prepared, preparedOptions) = Prepare(messages, options, scope);
        TraceRound(scope.Rounds, "request_sent", null);
        var response = await base.GetResponseAsync(prepared, preparedOptions, cancellationToken).ConfigureAwait(false);
        TraceRound(scope.Rounds, "response_received", response.Usage?.OutputTokenCount);

        scope.InputTokens += response.Usage?.InputTokenCount ?? 0;
        scope.OutputTokens += response.Usage?.OutputTokenCount ?? 0;
        foreach (var message in response.Messages)
        {
            if (message.Role != ChatRole.Assistant) continue;
            var text = message.Text;
            if (!string.IsNullOrWhiteSpace(text)) scope.AssistantText.Add(text);
        }
        WireCallRecovery.Recover(response, _core.Pipeline.ReserveEngineCallId);
        RegisterUnknownTools(response);
        return response;
    }

    private void TraceRound(int round, string status, long? outputTokens)
    {
        if (_core.Options.Trace is not Sensei.Engine.Trace.IRoundTraceSink) return;
        var turn = _core.CurrentTurn;
        _core.WriteTrace(new Sensei.Engine.Trace.TraceEntry {
            At = _core.Clock.GetUtcNow(), SessionId = _core.SessionId,
            TurnId = turn?.TurnId ?? 0, GenerationId = turn?.GenerationId ?? 0,
            Round = round, Tool = "engine:latency", Status = status,
            Arguments = outputTokens?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""
        });
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates()) yield return update;
    }

    /// <summary>S63: fresh instructions and the full learner block for this request; history keeps only the transcript (S129).</summary>
    private (List<ChatMessage> Messages, ChatOptions Options) Prepare(IEnumerable<ChatMessage> messages, ChatOptions? options, TurnScope scope)
    {
        // S77, S130: the copy that leaves the engine holds only text, function calls and function results, all sanitized; the stored
        // history is not touched. What the allow-list took out is traced by type name.
        var notes = new List<string>();
        var list = OutboundMessages.Sanitized(messages, _core.Secrets, notes, _resultsAsText, _core.Pipeline.ReserveEngineCallId);
        _core.TraceOutboundContent(notes);
        var prepared = options?.Clone() ?? new ChatOptions();
        prepared.Instructions = _core.Secrets.Sanitize(_renderer.RenderInstructions(scope));

        var learnerMessage = new ChatMessage(ChatRole.User, _renderer.RenderLearnerMessage(scope));
        var index = list.FindLastIndex(m => m.Role == ChatRole.User);
        if (index >= 0) list[index] = learnerMessage;
        else list.Add(learnerMessage);
        return (list, prepared);
    }

    /// <summary>S85: every function call whose name is not an offered tool gets a pass-through function, so the loop routes it to the engine.</summary>
    private void RegisterUnknownTools(ChatResponse response)
    {
        _dynamicTools.Clear();
        foreach (var call in response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>())
        {
            if (call.InformationalOnly || _offered.Contains(call.Name)) continue;
            if (_dynamicTools.Any(t => t.Name == call.Name)) continue;
            _dynamicTools.Add(new PassThroughFunction(call.Name));
        }
    }
}

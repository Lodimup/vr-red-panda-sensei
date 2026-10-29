using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Sensei.Engine.Events;
using Sensei.Engine.Llm;
using ChatMessageContentPart = OpenAI.Chat.ChatMessageContentPart;
using ChatToolCall = OpenAI.Chat.ChatToolCall;
using AssistantChatMessage = OpenAI.Chat.AssistantChatMessage;

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
    private readonly HashSet<string> _unavailable;

    public RoundLimitingChatClient(IChatClient inner, EngineCore core, ContextRenderer renderer, Func<TurnScope?> active,
        IList<AITool> dynamicTools, IEnumerable<string> offeredToolNames, Action<TurnScope, string> raiseState, bool resultsAsText = false, HashSet<string>? unavailable = null)
        : base(inner)
    {
        _core = core;
        _renderer = renderer;
        _active = active;
        _dynamicTools = dynamicTools;
        _offered = new HashSet<string>(offeredToolNames, StringComparer.Ordinal);
        _raiseState = raiseState;
        _resultsAsText = resultsAsText;
        _unavailable = unavailable ?? new HashSet<string>(StringComparer.Ordinal);
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
        // Keep four finished learner turns plus the current turn at the provider boundary.
        list = LearnerBoundaryReducer.Trim(list, scope.Request.EventKind is null ? 5 : 4);
        var currentIndex = list.FindLastIndex(m => m.Role == ChatRole.User);
        if (currentIndex > 0)
        {
            var previous = HistoryCompactor.Compact(list.Take(currentIndex).ToList());
            previous.AddRange(list.Skip(currentIndex));
            list = previous;
        }
        if (_resultsAsText) CompactFinishedCalls(list);
        _core.TraceOutboundContent(notes);
        var prepared = options?.Clone() ?? new ChatOptions();
        bool taskActive;
        lock (_core.Sync) taskActive = _core.Tasks.Active is not null;
        _unavailable.Clear();
        _unavailable.Add(taskActive ? "start_challenge" : "update_challenge");
        if (prepared.Tools is not null) prepared.Tools = prepared.Tools.Where(t => !_unavailable.Contains(t.Name)).ToList();
        prepared.Instructions = _core.Secrets.Sanitize(_renderer.RenderInstructions(scope));

        var learnerMessage = new ChatMessage(ChatRole.User, _renderer.RenderLearnerMessage(scope));
        var index = list.FindLastIndex(m => m.Role == ChatRole.User);
        if (index >= 0) list[index] = learnerMessage;
        else list.Add(learnerMessage);
        return (list, prepared);
    }

    private static void CompactFinishedCalls(List<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        var current = messages.FindLastIndex(m => m.Role == ChatRole.User);
        for (var i = 0; i < current; i++)
        {
            var message = messages[i];
            var calls = message.Contents.OfType<FunctionCallContent>().ToList();
            if (message.Role != ChatRole.Assistant || calls.Count == 0 || message.Contents.Any(c => c is not (FunctionCallContent or TextContent))) continue;
            var parts = message.Contents.OfType<TextContent>().Select(c => ChatMessageContentPart.CreateTextPart(c.Text)).ToList();
            var rawCalls = calls.Select(c => ChatToolCall.CreateFunctionToolCall(c.CallId, c.Name,
                BinaryData.FromBytes(SerializedRequestGuard.WriteJson(c.Arguments ?? new Dictionary<string, object?>(), OutboundGuard.MaxValues)))).ToList();
            var raw = parts.Count == 0 ? new AssistantChatMessage(rawCalls) : new AssistantChatMessage(parts);
            if (parts.Count > 0) foreach (var call in rawCalls) raw.ToolCalls.Add(call);
            var copy = message.Clone();
            copy.RawRepresentation = raw;
            messages[i] = copy;
        }
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

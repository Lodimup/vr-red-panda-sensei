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

    public RoundLimitingChatClient(IChatClient inner, EngineCore core, ContextRenderer renderer, Func<TurnScope?> active,
        IList<AITool> dynamicTools, IEnumerable<string> offeredToolNames, Action<TurnScope, string> raiseState)
        : base(inner)
    {
        _core = core;
        _renderer = renderer;
        _active = active;
        _dynamicTools = dynamicTools;
        _offered = new HashSet<string>(offeredToolNames, StringComparer.Ordinal);
        _raiseState = raiseState;
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
        var response = await base.GetResponseAsync(prepared, preparedOptions, cancellationToken).ConfigureAwait(false);

        scope.InputTokens += response.Usage?.InputTokenCount ?? 0;
        scope.OutputTokens += response.Usage?.OutputTokenCount ?? 0;
        foreach (var message in response.Messages)
        {
            if (message.Role != ChatRole.Assistant) continue;
            var text = message.Text;
            if (!string.IsNullOrWhiteSpace(text)) scope.AssistantText.Add(text);
        }
        RegisterUnknownTools(response);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates()) yield return update;
    }

    /// <summary>S63: fresh instructions and the full learner block (with notes) for this request; the stored copy stays compact.</summary>
    private (List<ChatMessage> Messages, ChatOptions Options) Prepare(IEnumerable<ChatMessage> messages, ChatOptions? options, TurnScope scope)
    {
        var list = messages.ToList();
        var prepared = options?.Clone() ?? new ChatOptions();
        prepared.Instructions = _renderer.RenderInstructions(scope);

        var learnerMessage = new ChatMessage(ChatRole.User, _renderer.RenderLearnerMessage(scope, includeNotes: true));
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

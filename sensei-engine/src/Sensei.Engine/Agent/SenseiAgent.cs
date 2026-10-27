using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sensei.Engine.Events;
using Sensei.Engine.Llm;
using Sensei.Engine.Speech;

namespace Sensei.Engine.Agent;

/// <summary>
/// Agent loop over Microsoft Agent Framework (S61–S68): a <see cref="ChatClientAgent"/> over the configured chat client with
/// the 16 tools, a context provider, per-call history persistence with a learner-boundary reducer, and a chat-client
/// stack that limits model rounds and routes every function call through the engine pipeline.
/// </summary>
/// <remarks>
/// Stack, outermost first: <c>ChatClientAgent</c> → function invocation (our invoker, serial) → per-service-call history
/// persistence → <see cref="RoundLimitingChatClient"/> → the provider client. The agent uses
/// <c>UseProvidedChatClientAsIs</c> so the framework does not wrap the stack a second time.
/// </remarks>
internal sealed class SenseiAgent : IAgentRunner
{
    private readonly EngineCore _core;
    private readonly ContextRenderer _renderer;
    private readonly ChatClientAgent? _agent;
    private readonly InMemoryChatHistoryProvider? _history;
    private ChatClientAgentSession? _session;
    private TurnScope? _active;
    private long _eventMark;

    public SenseiAgent(EngineCore core)
    {
        _core = core;
        _renderer = new ContextRenderer(core);
        if (core.Options.ChatClient is { } provider)
        {
            var dynamicTools = new List<AITool>();
            var offered = core.Tools.Standard;
            var limiter = new RoundLimitingChatClient(Guarded(provider, core, out var onWire), core, _renderer, () => _active, dynamicTools, offered.Select(t => t.Name), RaiseState, onWire);
            var persisted = new ChatClientBuilder(limiter).UsePerServiceCallChatHistoryPersistence().Build();
            var invoker = new EngineFunctionInvoker(core, () => _active, RaiseState);
            var functions = new FunctionInvokingChatClient(persisted)
            {
                AllowConcurrentInvocation = false,
                MaximumIterationsPerRequest = ContextRenderer.MaxRounds * 2,
                AdditionalTools = dynamicTools,
                FunctionInvoker = invoker.InvokeAsync,
            };
            _history = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
            {
                ChatReducer = new LearnerBoundaryReducer(),
                ProvideOutputMessageFilter = DropBlankAssistantMessages,
            });
            _agent = new ChatClientAgent(functions, new ChatClientAgentOptions
            {
                Name = "sensei",
                ChatOptions = new ChatOptions
                {
                    Instructions = SystemPrompt.Text,
                    Tools = offered.Cast<AITool>().ToList(),
                },
                ChatHistoryProvider = _history,
                AIContextProviders = new AIContextProvider[] { new SenseiContextProvider(_renderer, () => _active) },
                UseProvidedChatClientAsIs = true,
                RequirePerServiceCallChatHistoryPersistence = true,
                AllowConcurrentInvocation = false,
            });
        }
    }

    /// <summary>
    /// D6: the client the round limiter talks to. A client built by <see cref="ChatClientFactory"/> already ends in the byte guard of its
    /// request pipeline (it is found through <c>GetService</c>, also through wrappers that forward it); the engine only tells that guard
    /// which secrets it knows, and every function result goes to it as JSON text (<paramref name="onWire"/>). Any other client gets the
    /// serialized-request guard and the results as they are.
    /// </summary>
    private static IChatClient Guarded(IChatClient provider, EngineCore core, out bool onWire)
    {
        OutboundGuard? guard = null;
        try
        {
            guard = provider.GetService(typeof(OutboundGuard)) as OutboundGuard;
        }
        catch (Exception)
        {
            // A client that cannot be asked is a client without a guard.
        }
        onWire = guard is not null;
        if (guard is null) return new SerializedRequestGuard(provider, core.Secrets);
        guard.Attach(core.Secrets);
        return provider;
    }

    public async Task<TurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct)
    {
        var turn = request.Turn;
        if (_agent is null || _history is null)
            return Failed(turn, TurnErrorCodes.LlmNotConfigured, "no chat client is configured");

        CancellationToken generationToken;
        string? interrupted = null;
        lock (_core.Sync)
        {
            if (_core.CurrentGeneration != turn.GenerationId) return new TurnResult { TurnId = turn.TurnId, GenerationId = turn.GenerationId, Status = TurnStatuses.Cancelled };
            generationToken = _core.GenerationToken;
            if (request.PreviousTurnInterrupted)
                interrupted = _core.Utterances.All.LastOrDefault(u => !u.IsSynthetic && u.TurnId < turn.TurnId)?.Text;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, generationToken);
        var scope = new TurnScope(request, linked.Token, _eventMark) { InterruptedTranscript = interrupted };
        _active = scope;
        string status;
        string? errorCode = null;
        string? errorDetail = null;
        try
        {
            _session ??= (ChatClientAgentSession)await _agent.CreateSessionAsync(linked.Token).ConfigureAwait(false);
            PrepareHistory(_session);
            var learnerMessage = new ChatMessage(ChatRole.User, _renderer.RenderStoredLearnerMessage(scope));
            await _agent.RunAsync(learnerMessage, _session, null, linked.Token).ConfigureAwait(false);
            status = scope.BudgetExhausted ? TurnStatuses.BudgetExhausted : TurnStatuses.Completed;
            if (linked.IsCancellationRequested) status = TurnStatuses.Cancelled;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            status = TurnStatuses.Cancelled;
        }
        catch (Exception ex)
        {
            status = TurnStatuses.Failed;
            (errorCode, errorDetail) = ProviderErrors.Map(ex, _core.Secrets);
            // D6: a request the outbound guard refused to send is a provider_error whose cause is recorded in the trace.
            if (ProviderErrors.OutboundRejection(ex) is { } rejected) _core.TraceOutboundRejected(rejected);
        }
        finally
        {
            _active = null;
            _eventMark = Math.Max(_eventMark, scope.ShownEventsUpTo);
        }
        return BuildResult(scope, status, errorCode, errorDetail);
    }

    public void ResetConversation()
    {
        _session = null;
        lock (_core.Sync) _eventMark = _core.World.LastEventId;
    }

    /// <summary>S65: settle unanswered calls, then make room so the incoming learner message is the 12th at most.</summary>
    private void PrepareHistory(ChatClientAgentSession session)
    {
        var messages = _history!.GetMessages(session);
        HistorySettler.Settle(messages);
        _history.SetMessages(session, LearnerBoundaryReducer.Trim(messages, LearnerBoundaryReducer.MaxLearnerTurns - 1));
    }

    private void RaiseState(TurnScope scope, string state)
    {
        if (scope.LastState == state) return;
        scope.LastState = state;
        _core.Events.SenseiState(state);
    }

    private TurnResult BuildResult(TurnScope scope, string status, string? errorCode, string? errorDetail)
    {
        var turn = scope.Turn;
        var result = new TurnResult
        {
            TurnId = turn.TurnId,
            GenerationId = turn.GenerationId,
            Status = status,
            ErrorCode = errorCode,
            ErrorDetail = _core.Secrets.Sanitize(errorDetail),
            Rounds = scope.Rounds,
            InputTokens = scope.InputTokens,
            OutputTokens = scope.OutputTokens,
            UnspokenText = scope.AssistantText.Count == 0 ? null : _core.Secrets.Sanitize(string.Join("\n", scope.AssistantText)),
        };
        lock (_core.Sync)
        {
            foreach (var entry in _core.SpeechHistory.Entries.Where(e => e.TurnId == turn.TurnId))
            {
                result.Spoken.Add(new SpokenUtterance
                {
                    UtteranceId = entry.UtteranceId,
                    // S77: the report the host receives goes through the same sanitizer as every other string that leaves the engine.
                    Segments = entry.Segments.Select(s => new SpeechSegment(s.Lang, Clean(s.Text), CleanOrNull(s.Reading), CleanOrNull(s.Romaji), CleanOrNull(s.Translation))).ToList(),
                    Outcome = entry.Outcome,
                });
            }
        }
        return result;
    }

    private string Clean(string text) => _core.Secrets.Sanitize(text) ?? "";

    private string? CleanOrNull(string? text) => _core.Secrets.Sanitize(text);

    private static TurnResult Failed(Tools.TurnState turn, string code, string detail) => new()
    {
        TurnId = turn.TurnId,
        GenerationId = turn.GenerationId,
        Status = TurnStatuses.Failed,
        ErrorCode = code,
        ErrorDetail = detail,
    };

    /// <summary>The round limiter's empty answer (and an empty answer from a provider) is not history worth sending back.</summary>
    private static IEnumerable<ChatMessage> DropBlankAssistantMessages(IEnumerable<ChatMessage> messages) =>
        messages.Where(m => m.Role != ChatRole.Assistant
                            || m.Contents.OfType<FunctionCallContent>().Any()
                            || !string.IsNullOrWhiteSpace(m.Text));
}

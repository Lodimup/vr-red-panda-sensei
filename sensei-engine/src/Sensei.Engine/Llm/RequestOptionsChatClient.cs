using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;

namespace Sensei.Engine.Llm;

/// <summary>
/// S125: puts the settings' reasoning effort and output-token bound on every request that goes to the provider. It sits
/// directly on the provider client, under the agent stack, so a request the agent builds without those options still
/// carries them, and a caller that sets its own <see cref="ChatOptions.MaxOutputTokens"/> keeps it.
/// </summary>
/// <remarks>
/// OpenRouter takes the effort as <c>reasoning.effort</c>; measured on the default model, the plain OpenAI-style
/// <c>reasoning_effort</c> field changed nothing there. Every other provider gets the OpenAI chat-completions field
/// <c>reasoning_effort</c>.
/// </remarks>
internal sealed class RequestOptionsChatClient : DelegatingChatClient
{
    private readonly string _provider;
    private readonly string _effort;
    private readonly int _maxOutputTokens;
    private readonly OutboundGuard _guard;

    public RequestOptionsChatClient(IChatClient inner, LlmSettings settings, OutboundGuard guard)
        : base(inner)
    {
        _provider = settings.Provider;
        _effort = settings.ReasoningEffort;
        _maxOutputTokens = settings.MaxOutputTokens;
        _guard = guard;
    }

    /// <summary>
    /// D6: the byte guard in this client's request pipeline is a service of the client, so an engine (or any wrapper stacked on top, which
    /// forwards <c>GetService</c>) can find it and tell it which secrets to remove.
    /// </summary>
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(OutboundGuard) ? _guard : base.GetService(serviceType, serviceKey);

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Apply(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Apply(options), cancellationToken);

    /// <summary>The options as they go to the provider: the caller's options plus this client's effort and token bound.</summary>
    internal ChatOptions Apply(ChatOptions? options)
    {
        var applied = options?.Clone() ?? new ChatOptions();
        applied.MaxOutputTokens ??= _maxOutputTokens;
        var previous = applied.RawRepresentationFactory;
        applied.RawRepresentationFactory = client =>
        {
            var raw = previous?.Invoke(client) as ChatCompletionOptions ?? new ChatCompletionOptions();
            SetReasoning(raw);
            return raw;
        };
        return applied;
    }

#pragma warning disable SCME0001 // JsonPatch is the OpenAI SDK's way to send a field its typed options do not have.
    private void SetReasoning(ChatCompletionOptions raw)
    {
        if (_provider == LlmSettings.OpenRouter)
            raw.Patch.Set("$.reasoning"u8, BinaryData.FromString("{\"effort\":\"" + _effort + "\"}"));
        else
            raw.Patch.Set("$.reasoning_effort"u8, _effort);
    }
#pragma warning restore SCME0001
}

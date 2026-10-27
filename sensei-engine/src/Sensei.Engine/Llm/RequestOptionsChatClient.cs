using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;
using ChatTool = OpenAI.Chat.ChatTool;
using ChatToolChoice = OpenAI.Chat.ChatToolChoice;

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
/// <para>
/// Tools go out with their schema written compactly. The OpenAI adapter of Microsoft.Extensions.AI writes a function's
/// schema indented, and the 16 tool schemas are about two thirds of the input of every request, so the tools are handed
/// to the OpenAI client directly as <c>ChatTool</c>s with the schema text minified. The schema the engine validates
/// against is untouched, and the tool definitions the model sees are the same.
/// </para>
/// </remarks>
internal sealed class RequestOptionsChatClient : DelegatingChatClient
{
    private readonly string _provider;
    private readonly string _effort;
    private readonly int _maxOutputTokens;
    private readonly ConditionalWeakTable<AITool, string> _compactSchemas = new();

    public RequestOptionsChatClient(IChatClient inner, LlmSettings settings)
        : base(inner)
    {
        _provider = settings.Provider;
        _effort = settings.ReasoningEffort;
        _maxOutputTokens = settings.MaxOutputTokens;
    }

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Apply(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Apply(options), cancellationToken);

    /// <summary>The options as they go to the provider: the caller's options plus this client's effort, token bound and compact tools.</summary>
    internal ChatOptions Apply(ChatOptions? options)
    {
        var applied = options?.Clone() ?? new ChatOptions();
        applied.MaxOutputTokens ??= _maxOutputTokens;
        var functions = (applied.Tools ?? new List<AITool>()).OfType<AIFunctionDeclaration>().ToList();
        if (functions.Count > 0 && functions.Count == applied.Tools!.Count) applied.Tools = null;
        else functions.Clear();
        var previous = applied.RawRepresentationFactory;
        applied.RawRepresentationFactory = client =>
        {
            var raw = previous?.Invoke(client) as ChatCompletionOptions ?? new ChatCompletionOptions();
            foreach (var function in functions)
                raw.Tools.Add(ChatTool.CreateFunctionTool(function.Name, function.Description, BinaryData.FromString(CompactSchema(function))));
            if (functions.Count > 0) raw.ToolChoice ??= ChatToolChoice.CreateAutoChoice();
            SetReasoning(raw);
            return raw;
        };
        return applied;
    }

    private string CompactSchema(AIFunctionDeclaration function) =>
        _compactSchemas.GetValue(function, f =>
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
                ((AIFunctionDeclaration)f).JsonSchema.WriteTo(writer);
            return Encoding.UTF8.GetString(stream.ToArray());
        });

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

using System;
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Sensei.Engine.Llm;

/// <summary>Builds the chat client from <see cref="LlmSettings"/> (S6).</summary>
public static class ChatClientFactory
{
    /// <summary>
    /// S6: an <see cref="IChatClient"/> backed by the OpenAI chat-completions client, pointed at the settings' base URL,
    /// model and bearer key.
    /// </summary>
    public static IChatClient Create(LlmSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        var options = new OpenAIClientOptions { Endpoint = new Uri(settings.BaseUrl, UriKind.Absolute) };
        var client = new ChatClient(settings.Model, new ApiKeyCredential(settings.ApiKey), options);
        return client.AsIChatClient();
    }
}

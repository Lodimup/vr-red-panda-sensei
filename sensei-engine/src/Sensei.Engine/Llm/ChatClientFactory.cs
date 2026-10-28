using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Llm;

/// <summary>Builds the chat client from <see cref="LlmSettings"/> (S6).</summary>
public static class ChatClientFactory
{
    /// <summary>
    /// S6: an <see cref="IChatClient"/> backed by the OpenAI chat-completions client, pointed at the settings' base URL,
    /// model and bearer key. S125: every request it sends carries the settings' reasoning effort and output-token bound. D6: the
    /// client's request pipeline ends in an <see cref="OutboundGuard"/> that reads the body that is about to be sent and removes the key and
    /// every credential-pattern match from every string and property name in it; a body it cannot read is not sent.
    /// </summary>
    public static IChatClient Create(LlmSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        var guard = new OutboundGuard(new Secrets(new[] { settings.ApiKey }));
        var options = new OpenAIClientOptions { Endpoint = new Uri(settings.BaseUrl, UriKind.Absolute) };
        options.AddPolicy(guard, PipelinePosition.BeforeTransport);
        var client = new ChatClient(settings.Model, new ApiKeyCredential(settings.ApiKey), options);
        return new RequestOptionsChatClient(client.AsIChatClient(), settings, guard);
    }
}

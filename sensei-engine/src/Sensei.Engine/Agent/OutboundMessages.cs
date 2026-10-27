using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Agent;

/// <summary>
/// S77: nothing that holds a known secret or a credential-pattern match is ever sent to the provider. The conversation history keeps
/// what the provider said and what the model asked for; this makes the copy that goes out on the next request: text, reasoning text,
/// the name, id and arguments of every function call and the result of every function call, each through the one sanitizer.
/// The history itself is not changed.
/// </summary>
internal static class OutboundMessages
{
    public static List<ChatMessage> Sanitized(IEnumerable<ChatMessage> messages, Secrets secrets) =>
        messages.Select(m => Sanitized(m, secrets)).ToList();

    private static ChatMessage Sanitized(ChatMessage message, Secrets secrets)
    {
        var contents = message.Contents.Select(c => Sanitized(c, secrets)).ToList();
        return new ChatMessage(message.Role, contents)
        {
            AuthorName = secrets.Sanitize(message.AuthorName),
            MessageId = secrets.Sanitize(message.MessageId),
            CreatedAt = message.CreatedAt,
        };
    }

    private static AIContent Sanitized(AIContent content, Secrets secrets)
    {
        switch (content)
        {
            case TextContent text:
                return new TextContent(secrets.Sanitize(text.Text));
            case TextReasoningContent reasoning:
                return new TextReasoningContent(secrets.Sanitize(reasoning.Text));
            case FunctionCallContent call:
            {
                IDictionary<string, object?>? arguments = null;
                if (call.Arguments is not null)
                    arguments = (IDictionary<string, object?>)JsonRedaction.SanitizedValue(new Dictionary<string, object?>(call.Arguments), secrets)!;
                return new FunctionCallContent(secrets.Sanitize(call.CallId) ?? "", secrets.Sanitize(call.Name) ?? "", arguments)
                {
                    InformationalOnly = call.InformationalOnly,
                };
            }
            case FunctionResultContent result:
                return new FunctionResultContent(secrets.Sanitize(result.CallId) ?? "", JsonRedaction.SanitizedValue(result.Result, secrets));
            default:
                return content;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;
using Sensei.Engine.Llm;
using Sensei.Engine.Tools.Schema;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Agent;

/// <summary>
/// S77, S130: nothing that holds a known secret or a credential-pattern match is ever sent to the provider, and nothing but text,
/// function calls and function results is sent at all. The conversation history keeps what the provider said and what the model
/// asked for; this makes the copy that goes out on the next request. It is an allow-list: a message keeps only
/// <see cref="TextContent"/>, <see cref="FunctionCallContent"/> and <see cref="FunctionResultContent"/>, rebuilt from sanitized
/// strings (the text, the id, name and arguments of a call, the id and result of a result); every other content type (reasoning,
/// error, URI, data, usage, anything a provider or a later framework version adds) is removed before the request is built, and its
/// type name is reported to the caller for the trace. A message left with no content by that is dropped. The history itself is not
/// changed.
/// </summary>
internal static class OutboundMessages
{
    /// <summary>
    /// The copy of <paramref name="messages"/> that may be sent; <paramref name="notes"/> receives what was removed or replaced, by type name.
    /// This is the boundary of the in-memory preparation: a value that cannot be read or written anywhere in it (a disposed JSON element, a
    /// sequence whose enumerator throws, a dictionary that cannot be copied) is not sent, and the only thing that leaves is the engine's
    /// own <see cref="OutboundRejectedException"/> (reason <c>not_serializable</c>); the foreign exception and its text are dropped.
    /// </summary>
    public static List<ChatMessage> Sanitized(IEnumerable<ChatMessage> messages, Secrets secrets, List<string> notes, bool resultsAsText = false)
    {
        try
        {
            var copy = new List<ChatMessage>();
            foreach (var message in messages)
            {
                var sanitized = Sanitized(message, secrets, notes, resultsAsText);
                if (sanitized is not null) copy.Add(sanitized);
            }
            return copy;
        }
        catch (Exception)
        {
            throw new OutboundRejectedException(OutboundRejectedException.NotSerializable);
        }
    }

    private static ChatMessage? Sanitized(ChatMessage message, Secrets secrets, List<string> notes, bool resultsAsText)
    {
        var contents = new List<AIContent>();
        foreach (var content in message.Contents)
        {
            var kept = Sanitized(content, secrets, notes, resultsAsText);
            if (kept is not null) contents.Add(kept);
        }
        if (contents.Count == 0 && message.Contents.Count > 0) return null;
        return new ChatMessage(message.Role, contents)
        {
            AuthorName = secrets.Sanitize(message.AuthorName),
            MessageId = secrets.Sanitize(message.MessageId),
            CreatedAt = message.CreatedAt,
        };
    }

    private static AIContent? Sanitized(AIContent content, Secrets secrets, List<string> notes, bool resultsAsText)
    {
        switch (content)
        {
            case TextContent text:
                return new TextContent(secrets.Sanitize(text.Text));
            case FunctionCallContent call:
            {
                IDictionary<string, object?>? arguments = null;
                if (call.Arguments is not null)
                {
                    var clean = JsonRedaction.SanitizedValue(new Dictionary<string, object?>(call.Arguments), secrets, out var oversized);
                    arguments = clean as IDictionary<string, object?> ?? new Dictionary<string, object?>();
                    if (oversized) notes.Add("arguments over " + JsonRedaction.MaxNodes + " values (replaced by an empty object)");
                    else if (JsonRedaction.NestsDeeperThan(clean, ArgumentSchema.MaxArgumentDepth))
                    {
                        // Deeper than the tool-argument parser takes (S21): the call was answered invalid_arguments, so nothing executable is lost.
                        arguments = new Dictionary<string, object?>();
                        notes.Add("arguments nested over " + ArgumentSchema.MaxArgumentDepth + " levels (replaced by an empty object)");
                    }
                }
                // A call the provider named with an empty string (S85) goes back under a placeholder: the wire format needs some name, and
                // the call id is what pairs it with the engine result.
                var name = secrets.Sanitize(call.Name);
                return new FunctionCallContent(secrets.Sanitize(call.CallId) ?? "", string.IsNullOrEmpty(name) ? "unknown_tool" : name, arguments)
                {
                    InformationalOnly = call.InformationalOnly,
                };
            }
            case FunctionResultContent result:
            {
                var clean = JsonRedaction.SanitizedValue(result.Result, secrets, out var oversized);
                if (oversized) notes.Add("result over " + JsonRedaction.MaxNodes + " values (replaced)");
                return new FunctionResultContent(secrets.Sanitize(result.CallId) ?? "", resultsAsText ? AdapterResult(clean) : clean);
            }
            default:
                notes.Add("removed " + (content.GetType().FullName ?? content.GetType().Name));
                return null;
        }
    }

    /// <summary>
    /// The depth the SDK adapter's own serializer takes when it writes a function result (the framework default). A result that fits is
    /// handed over indented, as the adapter would have written it; a deeper one is handed over compact, because the adapter would have
    /// failed on it, and indenting a deep value costs space quadratic in its depth.
    /// </summary>
    private const int AdapterDepth = 64;

    /// <summary>
    /// What goes to the SDK adapter as the result of a call when the client is the engine's own (the adapter then never serializes a
    /// JSON element, node or collection itself, so its own depth limit cannot end a request outside this boundary). Null, text, booleans
    /// and finite numbers are what the adapter writes without a serializer and go on as they are. Anything else (the sanitized element,
    /// dictionary or list) is written here by the bounded iterative writer and goes as that JSON text, which the adapter hands on
    /// unchanged: for a result of ordinary depth the text is exactly what the adapter's serializer would have written.
    /// </summary>
    private static object? AdapterResult(object? clean)
    {
        if (clean is null or string || JsonRedaction.IsScalar(clean)) return clean;
        byte[] bytes;
        try
        {
            bytes = SerializedRequestGuard.WriteJson(clean, OutboundGuard.MaxValues, indented: true, maxDepth: AdapterDepth);
        }
        catch (OutboundRejectedException)
        {
            // Deeper than the adapter takes (or unwritable, which the compact write below rejects the same way).
            bytes = SerializedRequestGuard.WriteJson(clean, OutboundGuard.MaxValues);
        }
        return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
    }
}

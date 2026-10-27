using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Llm;

/// <summary>
/// D6: the byte-level guard for a chat client the engine did not build, one that has no request pipeline of its own to hook (a custom or
/// scripted <see cref="IChatClient"/>). The engine cannot see what such a client later puts on a wire, so it checks the closest thing
/// there is: the JSON form of what it hands over. Every loosely typed value of a message (the arguments of a call, the result of a call)
/// is written as JSON by an iterative writer (explicit stack, no recursion; a value of a type the writer does not know is written by the
/// standard serializer under its own runtime type, so a <see cref="Uri"/> or a record is seen as the text it would send), and that JSON is
/// walked by the same iterative sanitizer the wire guard uses (<see cref="OutboundGuard"/>: at most <see cref="OutboundGuard.MaxValues"/>
/// values in all, every string value and property name cleaned after unescaping, JSON held in a string treated as a string). A JSON
/// element or node inside such a value is walked node by node (<see cref="JsonRedaction.WriteJsonTree"/>), never printed to text first.
/// A value whose JSON needed cleaning reaches the client as the cleaned JSON element; every string of a message (text, ids, names, author)
/// is cleaned as text; messages with nothing to clean are passed on as they are. Every text value handed over (each text content, and the
/// instructions) is one value of the same budget, so the cap is for the whole request. A value that cannot be written, or a request over
/// the cap, is not passed on: <see cref="OutboundRejectedException"/>.
/// </summary>
/// <remarks>
/// The client built by <see cref="ChatClientFactory"/> does not need this wrapper: its guard is in the request pipeline and sees the real
/// body. The engine asks the client for that guard (<c>GetService</c>) and wraps only when it is absent. Only text, function call and
/// function result content can pass (S130); the round limiter above this client has already removed everything else.
/// </remarks>
internal sealed class SerializedRequestGuard : DelegatingChatClient
{
    private readonly Secrets _secrets;

    public SerializedRequestGuard(IChatClient inner, Secrets secrets)
        : base(inner)
    {
        _secrets = secrets;
    }

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // One budget for the whole request: the messages first, then the instructions.
        var budget = OutboundGuard.MaxValues;
        var guarded = Guarded(messages, ref budget);
        return base.GetResponseAsync(guarded, GuardedOptions(options, ref budget), cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var budget = OutboundGuard.MaxValues;
        var guarded = Guarded(messages, ref budget);
        return base.GetStreamingResponseAsync(guarded, GuardedOptions(options, ref budget), cancellationToken);
    }

    /// <summary>The messages that may be passed on; throws <see cref="OutboundRejectedException"/> if none may.</summary>
    internal List<ChatMessage> Guarded(IEnumerable<ChatMessage> messages)
    {
        var budget = OutboundGuard.MaxValues;
        return Guarded(messages, ref budget);
    }

    private List<ChatMessage> Guarded(IEnumerable<ChatMessage> messages, ref int budget)
    {
        var source = messages as List<ChatMessage> ?? messages.ToList();
        var result = new List<ChatMessage>(source.Count);
        var anyChanged = false;
        foreach (var message in source)
        {
            var guarded = GuardedMessage(message, ref budget);
            anyChanged |= !ReferenceEquals(guarded, message);
            result.Add(guarded);
        }
        return anyChanged ? result : source;
    }

    /// <summary>One string value handed to the client: it counts against the same budget as a structured value does.</summary>
    private static void Charge(ref int budget)
    {
        if (--budget < 0) throw new OutboundRejectedException(OutboundRejectedException.TooManyValues);
    }

    private ChatMessage GuardedMessage(ChatMessage message, ref int budget)
    {
        var changed = false;
        var contents = new List<AIContent>(message.Contents.Count);
        foreach (var content in message.Contents)
        {
            var guarded = GuardedContent(content, ref budget);
            changed |= !ReferenceEquals(guarded, content);
            contents.Add(guarded);
        }
        var author = _secrets.Sanitize(message.AuthorName);
        var id = _secrets.Sanitize(message.MessageId);
        changed |= !string.Equals(author, message.AuthorName, StringComparison.Ordinal) || !string.Equals(id, message.MessageId, StringComparison.Ordinal);
        if (!changed) return message;
        return new ChatMessage(message.Role, contents) { AuthorName = author, MessageId = id, CreatedAt = message.CreatedAt };
    }

    private AIContent GuardedContent(AIContent content, ref int budget)
    {
        switch (content)
        {
            case TextContent text:
            {
                Charge(ref budget);
                var clean = _secrets.Sanitize(text.Text);
                return string.Equals(clean, text.Text, StringComparison.Ordinal) ? text : new TextContent(clean);
            }
            case FunctionCallContent call:
            {
                var callId = _secrets.Sanitize(call.CallId) ?? "";
                var name = _secrets.Sanitize(call.Name) ?? "";
                IDictionary<string, object?>? arguments = call.Arguments;
                var argumentsChanged = false;
                if (call.Arguments is not null)
                {
                    var walked = Walk(call.Arguments, ref budget);
                    if (walked is { } element)
                    {
                        argumentsChanged = true;
                        arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                        if (element.ValueKind == JsonValueKind.Object)
                            foreach (var property in element.EnumerateObject()) arguments[property.Name] = property.Value.Clone();
                    }
                }
                if (!argumentsChanged && callId == call.CallId && name == call.Name) return call;
                return new FunctionCallContent(callId, name, arguments) { InformationalOnly = call.InformationalOnly };
            }
            case FunctionResultContent result:
            {
                var callId = _secrets.Sanitize(result.CallId) ?? "";
                var walked = Walk(result.Result, ref budget);
                if (walked is null && callId == result.CallId) return result;
                return new FunctionResultContent(callId, walked is { } element ? (object)element : result.Result);
            }
            default:
                throw new OutboundRejectedException(OutboundRejectedException.NotSerializable);
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> as JSON, walks that JSON, and returns the cleaned value when cleaning changed anything (null when it
    /// did not). Counts the values it saw against <paramref name="budget"/>.
    /// </summary>
    private JsonElement? Walk(object? value, ref int budget)
    {
        var json = WriteJson(value, budget);
        var status = JsonRedaction.SanitizeJson(json, JsonRedaction.Cleaner(_secrets), budget, out var cleaned, out var values, out var changed);
        if (status != JsonWalkStatus.Ok) throw OutboundRejectedException.For(status);
        budget -= values;
        if (!changed) return null;
        using var doc = JsonDocument.Parse(cleaned, new JsonDocumentOptions { MaxDepth = OutboundGuard.MaxValues + 1 });
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// The JSON text of a loosely typed value, written without recursion. Strings, numbers, booleans, JSON elements and nodes are
    /// written as they are; a dictionary with string keys is an object; any other sequence is an array; anything else is written by the
    /// serializer under its runtime type (see <see cref="JsonRedaction.ClrNestingLimit"/>). More than <paramref name="limit"/> values (a cycle included) or a value that cannot
    /// be written is <see cref="OutboundRejectedException"/>. <paramref name="indented"/> and <paramref name="maxDepth"/> let a caller that
    /// has to hand text to the SDK adapter reproduce the adapter's own layout and depth.
    /// </summary>
    internal static byte[] WriteJson(object? root, int limit, bool indented = false, int? maxDepth = null)
    {
        try
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = maxDepth ?? OutboundGuard.MaxValues + 1 }))
            {
                var count = 0;
                var open = new Stack<(IEnumerator Items, bool IsObject)>();
                try
                {
                    Write(root, writer, open, ref count, limit);
                    while (open.Count > 0)
                    {
                        var (items, isObject) = open.Peek();
                        if (!items.MoveNext())
                        {
                            open.Pop();
                            (items as IDisposable)?.Dispose();
                            if (isObject) writer.WriteEndObject();
                            else writer.WriteEndArray();
                            continue;
                        }
                        if (isObject)
                        {
                            var pair = (KeyValuePair<string, object?>)items.Current;
                            writer.WritePropertyName(pair.Key ?? "");
                            Write(pair.Value, writer, open, ref count, limit);
                        }
                        else
                        {
                            Write(items.Current, writer, open, ref count, limit);
                        }
                    }
                }
                finally
                {
                    foreach (var frame in open) (frame.Items as IDisposable)?.Dispose();
                }
            }
            return stream.ToArray();
        }
        catch (OutboundRejectedException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new OutboundRejectedException(OutboundRejectedException.NotSerializable);
        }
    }

    /// <summary>
    /// One value: a leaf is written at once; a container is opened and its items are queued on <paramref name="open"/>. A JSON element or
    /// node is written node by node by the shared iterative tree walk (<see cref="JsonRedaction.WriteJsonTree"/>), which counts every
    /// value in it on <paramref name="count"/>; no framework depth limit sits in front of it.
    /// </summary>
    private static void Write(object? value, Utf8JsonWriter writer, Stack<(IEnumerator Items, bool IsObject)> open, ref int count, int limit)
    {
        if (value is JsonElement || value is JsonNode)
        {
            var unused = false;
            if (!JsonRedaction.WriteJsonTree(value, writer, null, limit, ref count, ref unused))
                throw new OutboundRejectedException(OutboundRejectedException.TooManyValues);
            return;
        }
        if (++count > limit) throw new OutboundRejectedException(OutboundRejectedException.TooManyValues);
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case var scalar when JsonRedaction.WriteScalar(writer, scalar):
                break;
            case IDictionary<string, object?> map:
                writer.WriteStartObject();
                open.Push((map.GetEnumerator(), true));
                break;
            case IEnumerable sequence:
                writer.WriteStartArray();
                open.Push((sequence.GetEnumerator(), false));
                break;
            default:
                // A number, a Uri, a Guid, an enum, a record: whatever the serializer would send for it, written into a writer of its
                // own so the serializer's nesting guard (S133) is its framework default while the writer takes the request cap + 1.
                JsonRedaction.WriteClr(writer, value);
                break;
        }
    }

    /// <summary>The instructions are plain text the engine owns; they are one string value of the request whenever they are not null (an empty string included) and are cleaned as text, on a copy of the options.</summary>
    private ChatOptions? GuardedOptions(ChatOptions? options, ref int budget)
    {
        if (options?.Instructions is not { } instructions) return options;
        Charge(ref budget);
        var cleaned = _secrets.Sanitize(instructions);
        if (string.Equals(cleaned, instructions, StringComparison.Ordinal)) return options;
        var copy = options.Clone();
        copy.Instructions = cleaned;
        return copy;
    }
}

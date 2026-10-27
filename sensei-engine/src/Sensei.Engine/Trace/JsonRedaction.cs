using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sensei.Engine.Trace;

/// <summary>
/// S77: the JSON side of the sanitizing chokepoint. <see cref="Secrets.Sanitize"/> is the one primitive for a string; these helpers apply
/// it to every string value and property name of a JSON value, and to the loosely typed values a chat message carries.
/// </summary>
internal static class JsonRedaction
{
    /// <summary>Compact JSON of <paramref name="element"/> with every string value and property name sanitized. Duplicate names are kept.</summary>
    public static string SanitizedText(JsonElement element, Secrets secrets)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            Write(element, writer, secrets);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A detached copy of <paramref name="element"/> with every string value and property name sanitized.</summary>
    public static JsonElement Sanitized(JsonElement element, Secrets secrets)
    {
        using var doc = JsonDocument.Parse(SanitizedText(element, secrets));
        return doc.RootElement.Clone();
    }

    /// <summary>S77, S131: the most JSON values (or loosely typed values) one sanitizing pass visits; a value with more is replaced, never forwarded.</summary>
    internal const int MaxNodes = 10_000;

    /// <summary>
    /// S131: what the <c>arguments</c> field of a trace entry holds when the provider's argument text is not JSON the engine can walk
    /// within <see cref="MaxNodes"/> values. It is itself a JSON string.
    /// </summary>
    internal const string UnparseableArguments = "\"[unparseable arguments]\"";

    /// <summary>What replaces a loosely typed value that is over the node limit (a cycle ends here too).</summary>
    internal const string OversizedValue = "[oversized value]";

    /// <summary>
    /// S131: the sanitized text of the argument JSON a provider sent, for a trace. The text is parsed with a forward-only reader (no
    /// recursion, no depth limit; only the <see cref="MaxNodes"/> cap), every string value and property name is taken as the parser
    /// unescapes it, sanitized, and the JSON is written again. A document that does not parse, is blank, holds invalid UTF-16 or has
    /// more than <see cref="MaxNodes"/> values is replaced as a whole by <see cref="UnparseableArguments"/>: the raw provider text
    /// is never returned. Duplicate property names are kept. Never throws.
    /// </summary>
    public static string SanitizedArgumentsText(string? json, Secrets secrets)
    {
        if (string.IsNullOrWhiteSpace(json)) return UnparseableArguments;
        try
        {
            var bytes = new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(json!);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaxNodes + 1,
            });
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = MaxNodes + 1 }))
            {
                var nodes = 0;
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            writer.WritePropertyName(secrets.Sanitize(reader.GetString()) ?? "");
                            continue;
                        case JsonTokenType.EndObject:
                            writer.WriteEndObject();
                            continue;
                        case JsonTokenType.EndArray:
                            writer.WriteEndArray();
                            continue;
                    }
                    if (++nodes > MaxNodes) return UnparseableArguments;
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            writer.WriteStartObject();
                            break;
                        case JsonTokenType.StartArray:
                            writer.WriteStartArray();
                            break;
                        case JsonTokenType.String:
                            writer.WriteStringValue(secrets.Sanitize(reader.GetString()) ?? "");
                            break;
                        case JsonTokenType.Number:
                            writer.WriteRawValue(reader.ValueSpan);
                            break;
                        case JsonTokenType.True:
                            writer.WriteBooleanValue(true);
                            break;
                        case JsonTokenType.False:
                            writer.WriteBooleanValue(false);
                            break;
                        case JsonTokenType.Null:
                            writer.WriteNullValue();
                            break;
                        default:
                            return UnparseableArguments;
                    }
                }
                if (nodes == 0) return UnparseableArguments;
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (Exception)
        {
            // Not JSON, an unreadable string, or a writer limit: nothing of the provider's text goes to the trace.
            return UnparseableArguments;
        }
    }

    /// <summary>
    /// A sanitized copy of a loosely typed value (the arguments or result of a function call in a chat message): strings, JSON elements,
    /// JSON nodes, dictionaries and sequences are rebuilt, walked with an explicit stack and no depth limit; anything else (numbers,
    /// booleans) cannot hold text and is returned as is. A value with more than <see cref="MaxNodes"/> parts (a cycle included) is
    /// replaced as a whole by <see cref="OversizedValue"/> and <paramref name="oversized"/> is set: nothing is forwarded unsanitized.
    /// </summary>
    public static object? SanitizedValue(object? value, Secrets secrets) => SanitizedValue(value, secrets, out _);

    /// <inheritdoc cref="SanitizedValue(object?, Secrets)"/>
    public static object? SanitizedValue(object? value, Secrets secrets, out bool oversized)
    {
        oversized = false;
        var nodes = 0;
        var root = Rebuild(value, secrets, ref nodes, out var rootSource);
        if (rootSource is null) return root;

        var pending = new Stack<(object Source, object Target)>();
        pending.Push((rootSource, root!));
        while (pending.Count > 0)
        {
            var (source, target) = pending.Pop();
            if (source is IDictionary<string, object?> map)
            {
                var into = (Dictionary<string, object?>)target;
                foreach (var pair in map)
                {
                    var child = Rebuild(pair.Value, secrets, ref nodes, out var childSource);
                    if (nodes > MaxNodes)
                    {
                        oversized = true;
                        return OversizedValue;
                    }
                    into[secrets.Sanitize(pair.Key) ?? ""] = child;
                    if (childSource is not null) pending.Push((childSource, child!));
                }
            }
            else
            {
                var into = (List<object?>)target;
                foreach (var item in (IEnumerable)source)
                {
                    var child = Rebuild(item, secrets, ref nodes, out var childSource);
                    if (nodes > MaxNodes)
                    {
                        oversized = true;
                        return OversizedValue;
                    }
                    into.Add(child);
                    if (childSource is not null) pending.Push((childSource, child!));
                }
            }
        }
        return root;
    }

    /// <summary>
    /// One value: a leaf comes back sanitized (and <paramref name="source"/> is null); a dictionary or sequence comes back as an
    /// empty container of the same kind with <paramref name="source"/> set to what still has to be copied into it.
    /// </summary>
    private static object? Rebuild(object? value, Secrets secrets, ref int nodes, out object? source)
    {
        source = null;
        nodes++;
        switch (value)
        {
            case null:
                return null;
            case string text:
                return secrets.Sanitize(text) ?? "";
            case JsonElement element:
                return Sanitized(element, secrets);
            case JsonNode node:
                try
                {
                    using var doc = JsonDocument.Parse(node.ToJsonString());
                    return Sanitized(doc.RootElement, secrets);
                }
                catch (Exception)
                {
                    return OversizedValue;
                }
            case IDictionary<string, object?> map:
                source = map;
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            case IEnumerable sequence:
                source = sequence;
                return new List<object?>();
            default:
                return value;
        }
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer, Secrets secrets)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in element.EnumerateObject())
                {
                    writer.WritePropertyName(secrets.Sanitize(p.Name) ?? p.Name);
                    Write(p.Value, writer, secrets);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) Write(item, writer, secrets);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(secrets.Sanitize(element.GetString()) ?? "");
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

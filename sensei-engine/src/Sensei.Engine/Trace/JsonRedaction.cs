using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sensei.Engine.Trace;

/// <summary>What <see cref="JsonRedaction.SanitizeJson"/> found in the bytes it was given.</summary>
internal enum JsonWalkStatus
{
    /// <summary>One JSON document, walked whole within the value limit.</summary>
    Ok,

    /// <summary>Not a single valid JSON document (empty, malformed, trailing content, comments, invalid UTF-8 or UTF-16).</summary>
    NotJson,

    /// <summary>More JSON values than the limit allows; nothing was produced.</summary>
    TooManyValues,
}

/// <summary>
/// S77: the JSON side of the sanitizing chokepoint. <see cref="Secrets.Sanitize"/> is the one primitive for a string; these helpers apply
/// it to every string value and property name of a JSON value, and to the loosely typed values a chat message carries.
/// </summary>
internal static class JsonRedaction
{
    /// <summary>Compact JSON of <paramref name="element"/> with every string value and property name sanitized. Duplicate names are kept.</summary>
    public static string SanitizedText(JsonElement element, Secrets secrets)
    {
        var status = SanitizeTree(element, Cleaner(secrets), int.MaxValue, out var output, out _);
        if (status != JsonWalkStatus.Ok) throw new JsonException("the JSON value cannot be read as text");
        return Encoding.UTF8.GetString(output);
    }

    /// <summary>A detached copy of <paramref name="element"/> with every string value and property name sanitized.</summary>
    public static JsonElement Sanitized(JsonElement element, Secrets secrets)
    {
        using var doc = JsonDocument.Parse(SanitizedText(element, secrets), new JsonDocumentOptions { MaxDepth = WalkDepth(int.MaxValue) });
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
    /// D6: the most JSON values the final chokepoints walk: a provider request body, a messages JSON for a host-supplied client, a trace
    /// line. More and the bytes are not sent or written. Twice <see cref="MaxNodes"/>: the in-memory cap bounds one loosely typed value,
    /// the final cap bounds a whole request.
    /// </summary>
    internal const int MaxFinalValues = 20_000;

    /// <summary>What a trace line holds when the line itself could not be walked (D6); the reason is one of the engine's own words.</summary>
    internal static string RejectedTraceLine(JsonWalkStatus status) =>
        "{\"error_code\":\"trace_line_rejected\",\"detail\":\"trace line not written: "
        + (status == JsonWalkStatus.TooManyValues ? "too_many_values" : "not_json") + "\"}";

    /// <summary>
    /// D6: the final form of a serialized JSON line (one trace entry) that may be written to disk: parsed, every string value and property
    /// name cleaned after unescaping, written again. A line with nothing to clean is returned as it was. A line that is not one JSON
    /// document, or has more than <see cref="MaxFinalValues"/> values, is replaced by <see cref="RejectedTraceLine"/>: the original is never
    /// written. Never throws.
    /// </summary>
    public static string SanitizedJsonLine(string line, Secrets secrets)
    {
        try
        {
            var bytes = new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(line);
            var status = SanitizeJson(bytes, Cleaner(secrets), MaxFinalValues, out var output, out _, out var changed);
            if (status != JsonWalkStatus.Ok) return RejectedTraceLine(status);
            return changed ? Encoding.UTF8.GetString(output) : line;
        }
        catch (Exception)
        {
            return RejectedTraceLine(JsonWalkStatus.NotJson);
        }
    }

    /// <summary>The reader and writer depth for a walk of at most <paramref name="maxValues"/> values (a nesting level costs at least one value).</summary>
    private static int WalkDepth(int maxValues) => (int)Math.Min((long)maxValues + 1, 1_000_001);

    /// <summary><see cref="Secrets.Sanitize"/> as a function that never returns null.</summary>
    internal static Func<string, string> Cleaner(Secrets secrets) => text => secrets.Sanitize(text) ?? "";

    /// <summary>
    /// The one iterative JSON sanitizer: reads <paramref name="json"/> with a forward-only reader (no recursion, no depth limit beyond the
    /// value limit), takes every string value and property name as the reader unescapes it, passes it through <paramref name="clean"/>,
    /// and writes the document again with the same structure. Duplicate property names are kept and numbers keep their spelling. A value
    /// is each object, array, string, number or literal (property names are not counted); a document with more than
    /// <paramref name="maxValues"/> values yields <see cref="JsonWalkStatus.TooManyValues"/> and no output. A string that itself holds
    /// JSON is just a string: it is cleaned as text and never parsed again. <paramref name="changed"/> says whether any string or name
    /// came out different, so a caller can send the original bytes untouched when nothing needed cleaning. Never throws.
    /// </summary>
    internal static JsonWalkStatus SanitizeJson(ReadOnlySpan<byte> json, Func<string, string> clean, int maxValues,
        out byte[] output, out int values, out bool changed)
    {
        output = Array.Empty<byte>();
        values = 0;
        changed = false;
        try
        {
            var depth = WalkDepth(maxValues);
            var reader = new Utf8JsonReader(json, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = depth,
            });
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = depth }))
            {
                var count = 0;
                var differs = false;
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                        {
                            var name = reader.GetString() ?? "";
                            var cleanName = clean(name);
                            if (!string.Equals(name, cleanName, StringComparison.Ordinal)) differs = true;
                            writer.WritePropertyName(cleanName);
                            continue;
                        }
                        case JsonTokenType.EndObject:
                            writer.WriteEndObject();
                            continue;
                        case JsonTokenType.EndArray:
                            writer.WriteEndArray();
                            continue;
                    }
                    if (++count > maxValues) return JsonWalkStatus.TooManyValues;
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            writer.WriteStartObject();
                            break;
                        case JsonTokenType.StartArray:
                            writer.WriteStartArray();
                            break;
                        case JsonTokenType.String:
                        {
                            var text = reader.GetString() ?? "";
                            var cleanText = clean(text);
                            if (!string.Equals(text, cleanText, StringComparison.Ordinal)) differs = true;
                            writer.WriteStringValue(cleanText);
                            break;
                        }
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
                            return JsonWalkStatus.NotJson;
                    }
                }
                if (count == 0) return JsonWalkStatus.NotJson;
                values = count;
                changed = differs;
            }
            output = stream.ToArray();
            return JsonWalkStatus.Ok;
        }
        catch (Exception)
        {
            // Not JSON, an unreadable string, or a writer limit: the caller decides what replaces it.
            values = 0;
            changed = false;
            return JsonWalkStatus.NotJson;
        }
    }

    /// <summary>
    /// The iterative walk over a <see cref="JsonElement"/> or a <see cref="JsonNode"/> (<paramref name="root"/>, boxed): the way a JSON value
    /// that is already in memory is read, instead of printing it to text and reading that back. One value at a time, an explicit stack
    /// of enumerators, no recursion and no depth limit of the framework's own: a nesting level costs at least one value, so the value
    /// limit is the only bound. Every value, the root included, is added to <paramref name="count"/>; the walk stops with false as soon
    /// as the count passes <paramref name="maxValues"/>. Strings and property names are taken as they are read and passed through
    /// <paramref name="clean"/> (null leaves them as they are); <paramref name="changed"/> is set when one came out different. The writer
    /// needs <c>MaxDepth</c> of <paramref name="maxValues"/> + 1. A value that cannot be read or written (a disposed element, an
    /// undefined element, an unreadable string) throws: the caller decides what an unwritable value means. A node that holds a value of
    /// some other type is written by the node's own serializer and that text is cleaned by <see cref="SanitizeJson"/>.
    /// </summary>
    internal static bool WriteJsonTree(object root, Utf8JsonWriter writer, Func<string, string>? clean, int maxValues,
        ref int count, ref bool changed)
    {
        var open = new Stack<TreeFrame>();
        try
        {
            object? next = root;
            while (true)
            {
                if (++count > maxValues) return false;
                if (!WriteTreeValue(next, writer, clean, open, maxValues, ref count, ref changed)) return false;

                // The next value: the next item of the innermost open container, closing the containers that have no more.
                var found = false;
                while (open.Count > 0 && !found)
                {
                    var frame = open.Peek();
                    if (!frame.Items.MoveNext())
                    {
                        open.Pop();
                        (frame.Items as IDisposable)?.Dispose();
                        if (frame.Kind == TreeKind.ElementObject || frame.Kind == TreeKind.NodeObject) writer.WriteEndObject();
                        else writer.WriteEndArray();
                        continue;
                    }
                    switch (frame.Kind)
                    {
                        case TreeKind.ElementArray:
                            next = ((IEnumerator<JsonElement>)frame.Items).Current;
                            break;
                        case TreeKind.ElementObject:
                        {
                            var property = ((IEnumerator<JsonProperty>)frame.Items).Current;
                            WriteTreeString(property.Name, writer, clean, true, ref changed);
                            next = property.Value;
                            break;
                        }
                        case TreeKind.NodeArray:
                            next = ((IEnumerator<JsonNode?>)frame.Items).Current;
                            break;
                        default:
                        {
                            var pair = ((IEnumerator<KeyValuePair<string, JsonNode?>>)frame.Items).Current;
                            WriteTreeString(pair.Key, writer, clean, true, ref changed);
                            next = pair.Value;
                            break;
                        }
                    }
                    found = true;
                }
                if (!found) return true;
            }
        }
        finally
        {
            foreach (var frame in open) (frame.Items as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// <see cref="WriteJsonTree"/> into a new document: the sanitized bytes of an element or node, and the number of values in it.
    /// <see cref="JsonWalkStatus.TooManyValues"/> (and no output) when it has more than <paramref name="maxValues"/>. Throws for a value
    /// that cannot be read or written.
    /// </summary>
    internal static JsonWalkStatus SanitizeTree(object root, Func<string, string> clean, int maxValues, out byte[] output, out int values)
    {
        output = Array.Empty<byte>();
        values = 0;
        var count = 0;
        var changed = false;
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = WalkDepth(maxValues) }))
        {
            if (!WriteJsonTree(root, writer, clean, maxValues, ref count, ref changed)) return JsonWalkStatus.TooManyValues;
        }
        output = stream.ToArray();
        values = count;
        return JsonWalkStatus.Ok;
    }

    /// <summary>What a <see cref="TreeFrame"/> is walking.</summary>
    private enum TreeKind : byte
    {
        ElementArray,
        ElementObject,
        NodeArray,
        NodeObject,
    }

    /// <summary>One open container of <see cref="WriteJsonTree"/>: its (boxed) enumerator and what kind it is.</summary>
    private readonly struct TreeFrame
    {
        public TreeFrame(TreeKind kind, IEnumerator items)
        {
            Kind = kind;
            Items = items;
        }

        public TreeKind Kind { get; }

        public IEnumerator Items { get; }
    }

    /// <summary>Writes one value of the tree, or opens it (a container is pushed on <paramref name="open"/>). False when it does not fit the limit.</summary>
    private static bool WriteTreeValue(object? value, Utf8JsonWriter writer, Func<string, string>? clean, Stack<TreeFrame> open,
        int maxValues, ref int count, ref bool changed)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return true;
            case JsonElement element:
                WriteTreeElement(element, writer, clean, open, ref changed);
                return true;
            case JsonObject obj:
                writer.WriteStartObject();
                open.Push(new TreeFrame(TreeKind.NodeObject, ((IEnumerable<KeyValuePair<string, JsonNode?>>)obj).GetEnumerator()));
                return true;
            case JsonArray array:
                writer.WriteStartArray();
                open.Push(new TreeFrame(TreeKind.NodeArray, ((IEnumerable<JsonNode?>)array).GetEnumerator()));
                return true;
            case JsonValue leaf:
                if (leaf.TryGetValue(out JsonElement inner))
                {
                    WriteTreeElement(inner, writer, clean, open, ref changed);
                    return true;
                }
                if (leaf.TryGetValue(out string? text))
                {
                    WriteTreeString(text ?? "", writer, clean, false, ref changed);
                    return true;
                }
                return WriteTreeLeaf(leaf, writer, clean, maxValues, ref count, ref changed);
            default:
                throw new JsonException("the value is neither a JSON element nor a JSON node");
        }
    }

    private static void WriteTreeElement(JsonElement element, Utf8JsonWriter writer, Func<string, string>? clean, Stack<TreeFrame> open,
        ref bool changed)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                open.Push(new TreeFrame(TreeKind.ElementObject, element.EnumerateObject()));
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                open.Push(new TreeFrame(TreeKind.ElementArray, element.EnumerateArray()));
                break;
            case JsonValueKind.String:
                WriteTreeString(element.GetString() ?? "", writer, clean, false, ref changed);
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("the JSON element has no value");
        }
    }

    private static void WriteTreeString(string text, Utf8JsonWriter writer, Func<string, string>? clean, bool isName, ref bool changed)
    {
        var cleaned = clean is null ? text : clean(text);
        if (!string.Equals(text, cleaned, StringComparison.Ordinal)) changed = true;
        if (isName) writer.WritePropertyName(cleaned);
        else writer.WriteStringValue(cleaned);
    }

    /// <summary>
    /// A node that holds a value of some other type (a number, a <see cref="Uri"/>, a record): the node's own serializer writes it, and
    /// that text is cleaned and counted like any other document. False when it does not fit the limit.
    /// </summary>
    private static bool WriteTreeLeaf(JsonValue leaf, Utf8JsonWriter writer, Func<string, string>? clean, int maxValues, ref int count,
        ref bool changed)
    {
        // The value itself was counted once already.
        var status = SanitizeJson(Encoding.UTF8.GetBytes(leaf.ToJsonString()), clean ?? (text => text), maxValues - (count - 1),
            out var output, out var values, out var leafChanged);
        if (status == JsonWalkStatus.TooManyValues) return false;
        if (status != JsonWalkStatus.Ok) throw new JsonException("the JSON value cannot be read");
        count += values - 1;
        if (leafChanged) changed = true;
        writer.WriteRawValue(output, skipInputValidation: true);
        return true;
    }

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
            var status = SanitizeJson(bytes, Cleaner(secrets), MaxNodes, out var output, out _, out _);
            return status == JsonWalkStatus.Ok ? Encoding.UTF8.GetString(output) : UnparseableArguments;
        }
        catch (Exception)
        {
            // An unreadable string: nothing of the provider's text goes to the trace.
            return UnparseableArguments;
        }
    }

    /// <summary>
    /// A sanitized copy of a loosely typed value (the arguments or result of a function call in a chat message): strings, JSON elements,
    /// JSON nodes, dictionaries and sequences are rebuilt, walked with an explicit stack and no depth limit; anything else (numbers,
    /// booleans) cannot hold text and is returned as is. A value with more than <see cref="MaxNodes"/> parts (a cycle included, and the
    /// values inside a JSON element or node count as parts like any others) is replaced as a whole by <see cref="OversizedValue"/> and
    /// <paramref name="oversized"/> is set: nothing is forwarded unsanitized.
    /// </summary>
    public static object? SanitizedValue(object? value, Secrets secrets) => SanitizedValue(value, secrets, out _);

    /// <inheritdoc cref="SanitizedValue(object?, Secrets)"/>
    public static object? SanitizedValue(object? value, Secrets secrets, out bool oversized)
    {
        oversized = false;
        var nodes = 0;
        var root = Rebuild(value, secrets, ref nodes, out var rootSource);
        if (nodes > MaxNodes)
        {
            oversized = true;
            return OversizedValue;
        }
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
    /// empty container of the same kind with <paramref name="source"/> set to what still has to be copied into it. A JSON element or
    /// node is walked at once and every value inside it is added to <paramref name="nodes"/>, so it shares the limit with the rest.
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
            case JsonElement:
            case JsonNode:
                return RebuildJson(value, secrets, ref nodes);
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

    /// <summary>
    /// The sanitized copy of one JSON value (<paramref name="json"/>, a boxed element or node) held inside a loosely typed value. It is
    /// walked node by node by <see cref="SanitizeTree"/>, never printed to text first, and its values are counted into
    /// <paramref name="nodes"/> (the caller already counted the value itself once); one that does not fit in what is left of
    /// <see cref="MaxNodes"/> pushes the count over the limit, which the caller turns into the whole-value replacement. A value that
    /// cannot be read throws and is never replaced quietly.
    /// </summary>
    private static object? RebuildJson(object json, Secrets secrets, ref int nodes)
    {
        var room = MaxNodes - (nodes - 1);
        var status = SanitizeTree(json, Cleaner(secrets), room, out var output, out var values);
        if (status == JsonWalkStatus.TooManyValues)
        {
            nodes = MaxNodes + 1;
            return OversizedValue;
        }
        nodes += values - 1;
        using var doc = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = WalkDepth(MaxNodes) });
        return doc.RootElement.Clone();
    }
}

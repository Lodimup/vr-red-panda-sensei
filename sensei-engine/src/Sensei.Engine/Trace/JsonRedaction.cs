using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

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

    /// <summary>The reader, writer and document depth for a walk of at most <paramref name="maxValues"/> values (a nesting level costs at least one value).</summary>
    internal static int WalkDepth(int maxValues) => (int)Math.Min((long)maxValues + 1, 1_000_001);

    /// <summary>
    /// The one depth that is NOT raised to the value cap: how deeply the standard serializer may nest the properties of a CLR object. It is
    /// the framework default, and it is the only guard there is: System.Text.Json recurses on the call stack with no stack check of its
    /// own. Measured on this runtime: a cyclic object (<c>class C { C Self }</c>) serialized with <c>MaxDepth</c> 10 001 ends the process
    /// (stack overflow, nothing can catch it) on a 1.5 MiB thread stack, with 20 001 on a 1 MiB stack; with 64 it is a
    /// <see cref="JsonException"/>. A deep JSON element held by such an object needs no more than this: it is written by the writer
    /// (iteratively), whose <c>MaxDepth</c> is the value cap + 1. A deep JSON node is not safe to leave to the framework: the node's own
    /// <c>WriteTo</c> recurses once per level (measured: a record holding a node nested 20 000 deep ends the process on a 1 MiB stack),
    /// so inside such an object every node goes through <see cref="NodeConverter{T}"/>, the same iterative walk as everywhere else.
    /// </summary>
    internal const int ClrNestingLimit = 64;

    /// <summary>The standard serializer under the runtime type of a value, with <see cref="ClrNestingLimit"/>. Built on first use: a runtime that cannot build it only loses CLR values of an unknown type, which are then refused.</summary>
    private static readonly Lazy<JsonTypeInfo<object>?> ClrInfo = new(CreateClrInfo);

    private static JsonTypeInfo<object>? CreateClrInfo()
    {
        try
        {
            var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { MaxDepth = ClrNestingLimit };
            AddNodeConverters(options);
            return (JsonTypeInfo<object>)options.GetTypeInfo(typeof(object));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The options for writing a node that holds a CLR value (<see cref="WriteTreeLeaf"/>): the serializer's own nesting guard and the iterative node converters.</summary>
    private static readonly Lazy<JsonSerializerOptions?> NodeLeafOptions = new(CreateNodeLeafOptions);

    private static JsonSerializerOptions? CreateNodeLeafOptions()
    {
        try
        {
            var options = new JsonSerializerOptions { MaxDepth = ClrNestingLimit, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
            AddNodeConverters(options);
            return options;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void AddNodeConverters(JsonSerializerOptions options)
    {
        options.Converters.Add(new NodeConverter<JsonNode>());
        options.Converters.Add(new NodeConverter<JsonObject>());
        options.Converters.Add(new NodeConverter<JsonArray>());
        options.Converters.Add(new NodeConverter<JsonValue>());
    }

    /// <summary>
    /// How many CLR-backed leaves (a <see cref="JsonValue"/> that holds an object) may be in the middle of being serialized at once on one
    /// thread. A leaf is written by the serializer, whose own nesting count starts at zero each time, and an object it writes may hold a node
    /// that holds such a leaf again; each of those hand-overs is a new serializer and a new walk, so neither the serializer's nesting limit
    /// (<see cref="ClrNestingLimit"/>) nor the walk's value limit sees the chain. A cycle through a node (an object that holds a node that holds
    /// the object) ended the process with a stack overflow. The ninth leaf in flight is refused as not serializable.
    /// </summary>
    internal const int ClrReentryLimit = 8;

    /// <summary>
    /// One CLR-backed leaf that is being serialized (<see cref="WriteTreeLeaf"/>): how deep the chain of such leaves is, and the part of the
    /// value limit that is already used when it starts, so the walks it causes below the first (<see cref="NodeConverter{T}"/>) draw on the
    /// walk that is above them instead of each starting with a limit of its own. Kept per thread (a walk never awaits), nothing is shared
    /// between threads.
    /// </summary>
    private sealed class ReentryFrame
    {
        public ReentryFrame(ReentryFrame? parent, int maxValues, int used)
        {
            Parent = parent;
            Depth = parent is null ? 1 : parent.Depth + 1;
            MaxValues = maxValues;
            Used = used;
        }

        public ReentryFrame? Parent { get; }

        public int Depth { get; }

        /// <summary>The value limit of the walk that holds the leaf.</summary>
        public int MaxValues { get; }

        /// <summary>The values that walk had counted before the leaf's own, the leaf's value itself excluded.</summary>
        public int Used { get; }

        /// <summary>A nested walk stopped because the limit above it was used up, not because of its own limit: the leaf is then over the limit, not unwritable.</summary>
        public bool Exceeded { get; set; }
    }

    [ThreadStatic]
    private static ReentryFrame? t_reentry;

    /// <summary>Writes a JSON node held by a CLR value with <see cref="WriteJsonTree"/> (an explicit stack, no recursion, at most <see cref="MaxFinalValues"/> values) instead of the node's own recursive <c>WriteTo</c>.</summary>
    private sealed class NodeConverter<T> : JsonConverter<T> where T : JsonNode
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("nodes are only written here");

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            // The first walk below a leaf has a limit of its own. A walk that is re-entered (a leaf inside such a walk, and so on down)
            // continues the count of the walk above it, within that walk's limit.
            var above = t_reentry is { Depth: > 1 } reentered ? reentered : null;
            var count = above?.Used ?? 0;
            var limit = above is null ? MaxFinalValues : Math.Min(above.MaxValues, count + MaxFinalValues);
            var changed = false;
            if (WriteJsonTree(value, writer, null, limit, ref count, ref changed)) return;
            if (above is not null && above.MaxValues < above.Used + MaxFinalValues) above.Exceeded = true;
            throw new JsonException("the JSON node has too many values");
        }
    }

    /// <summary>A boolean or a finite number: the only CLR values that need no serializer (anything else can hold text, a getter that throws, or a nesting).</summary>
    internal static bool IsScalar(object? value) => value switch
    {
        bool or sbyte or byte or short or ushort or int or uint or long or ulong or decimal => true,
        float single => !float.IsNaN(single) && !float.IsInfinity(single),
        double number => !double.IsNaN(number) && !double.IsInfinity(number),
        _ => false,
    };

    /// <summary>
    /// Writes a boolean or a finite number (<see cref="IsScalar"/>) as the serializer would, by the writer's own typed method, so an
    /// indenting writer lays it out like any other value (a raw value would not be indented); false for anything else.
    /// </summary>
    internal static bool WriteScalar(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case bool flag: writer.WriteBooleanValue(flag); return true;
            case sbyte number: writer.WriteNumberValue(number); return true;
            case byte number: writer.WriteNumberValue(number); return true;
            case short number: writer.WriteNumberValue(number); return true;
            case ushort number: writer.WriteNumberValue(number); return true;
            case int number: writer.WriteNumberValue(number); return true;
            case uint number: writer.WriteNumberValue(number); return true;
            case long number: writer.WriteNumberValue(number); return true;
            case ulong number: writer.WriteNumberValue(number); return true;
            case decimal number: writer.WriteNumberValue(number); return true;
            case float number when IsScalar(value): writer.WriteNumberValue(number); return true;
            case double number when IsScalar(value): writer.WriteNumberValue(number); return true;
            default: return false;
        }
    }

    /// <summary>
    /// The JSON of a value of any type, serialized under its runtime type into a writer of its own (so the serializer's nesting count starts
    /// at zero and the writer's <c>MaxDepth</c> is the request cap + 1). Reads every property, so a getter that throws throws here.
    /// </summary>
    internal static byte[] SerializeClr(object value)
    {
        JsonTypeInfo<object> info = ClrInfo.Value ?? throw new JsonException("the value cannot be serialized on this runtime");
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = WalkDepth(MaxFinalValues) }))
        {
            JsonSerializer.Serialize(writer, value, info);
        }
        return stream.ToArray();
    }

    /// <summary>Writes <paramref name="value"/> (not JSON, not a scalar) as the JSON <see cref="SerializeClr"/> gives for it.</summary>
    internal static void WriteClr(Utf8JsonWriter writer, object value) => writer.WriteRawValue(SerializeClr(value), skipInputValidation: true);

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
    /// <para>
    /// <paramref name="cleanCallId"/> is the one exception to "every string": when it is given, the body is read as an OpenAI chat-completions
    /// request and the string values of <c>id</c> in <c>messages[].tool_calls[]</c> items and of <c>tool_call_id</c> in <c>messages[]</c> items
    /// (S133, call ids) go through it instead of <paramref name="clean"/>, at exactly those two places and nowhere else. Property names, and
    /// strings that hold JSON, are never call ids.
    /// </para>
    /// </summary>
    internal static JsonWalkStatus SanitizeJson(ReadOnlySpan<byte> json, Func<string, string> clean, int maxValues,
        out byte[] output, out int values, out bool changed, Func<string, string>? cleanCallId = null)
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
                var position = cleanCallId is null ? null : new CallIdPosition();
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                        {
                            var name = reader.GetString() ?? "";
                            position?.Name(name);
                            var cleanName = clean(name);
                            if (!string.Equals(name, cleanName, StringComparison.Ordinal)) differs = true;
                            writer.WritePropertyName(cleanName);
                            continue;
                        }
                        case JsonTokenType.EndObject:
                            position?.End();
                            writer.WriteEndObject();
                            continue;
                        case JsonTokenType.EndArray:
                            position?.End();
                            writer.WriteEndArray();
                            continue;
                    }
                    if (++count > maxValues) return JsonWalkStatus.TooManyValues;
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            position?.Start(isObject: true);
                            writer.WriteStartObject();
                            break;
                        case JsonTokenType.StartArray:
                            position?.Start(isObject: false);
                            writer.WriteStartArray();
                            break;
                        case JsonTokenType.String:
                        {
                            var text = reader.GetString() ?? "";
                            var cleanText = position is { IsCallId: true } ? cleanCallId!(text) : clean(text);
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
    /// Where the forward-only reader of <see cref="SanitizeJson"/> stands in an OpenAI chat-completions request body, as far as the two call
    /// id fields need (S133): the kind of each of the first five open containers and the last property name read in each. It recognizes a
    /// string value that is <c>messages[i].tool_call_id</c> or <c>messages[i].tool_calls[j].id</c> of the root object, and nothing else:
    /// the same name anywhere else in the body (another depth, another parent, an array element, a property name) is an ordinary string.
    /// </summary>
    private sealed class CallIdPosition
    {
        private const int Tracked = 5;
        private const byte ObjectKind = 1;
        private const byte ArrayKind = 2;

        private readonly byte[] _kind = new byte[Tracked + 1];
        private readonly string?[] _name = new string?[Tracked + 1];
        private int _level;

        /// <summary>A container opens: it is one level deeper, with no property name read in it yet.</summary>
        public void Start(bool isObject)
        {
            _level++;
            if (_level > Tracked) return;
            _kind[_level] = isObject ? ObjectKind : ArrayKind;
            _name[_level] = null;
        }

        /// <summary>The innermost container closes.</summary>
        public void End()
        {
            if (_level <= Tracked)
            {
                _kind[_level] = 0;
                _name[_level] = null;
            }
            _level--;
        }

        /// <summary>A property name was read in the innermost container (the name as the reader unescaped it).</summary>
        public void Name(string name)
        {
            if (_level <= Tracked) _name[_level] = name;
        }

        /// <summary>True when a string value read now is a call id.</summary>
        public bool IsCallId =>
            _level >= 3 && InMessage && (
                (_level == 3 && _name[3] == "tool_call_id")
                || (_level == 5 && _name[3] == "tool_calls" && _kind[4] == ArrayKind && _kind[5] == ObjectKind && _name[5] == "id"));

        /// <summary>Inside an item of the <c>messages</c> array of the root object.</summary>
        private bool InMessage => _kind[1] == ObjectKind && _name[1] == "messages" && _kind[2] == ArrayKind && _kind[3] == ObjectKind;
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
                // A raw value is written without the writer's indentation; an element writes its own number, spelling kept, formatted.
                if (writer.Options.Indented) element.WriteTo(writer);
                else writer.WriteRawValue(element.GetRawText());
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

    /// <summary>The options for printing a scalar node: the request cap + 1 deep (nothing in a number or a boolean can recurse).</summary>
    private static readonly JsonSerializerOptions ScalarMaxDepthOptions = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { MaxDepth = WalkDepth(MaxFinalValues) };

    /// <summary>
    /// A node that holds a value of some other type (a number, a <see cref="Uri"/>, a record): it is written as JSON, and that text is
    /// cleaned and counted like any other document. A number or boolean is printed by the node. Any other value is written by the node's
    /// own serializer straight into a writer of its own, whose <c>MaxDepth</c> is the request cap + 1 while the serializer's nesting stays
    /// <see cref="ClrNestingLimit"/>: a deep JSON element inside a record is written, a cyclic object is a <see cref="JsonException"/> and
    /// never a stack overflow. False when it does not fit the limit.
    /// </summary>
    private static bool WriteTreeLeaf(JsonValue leaf, Utf8JsonWriter writer, Func<string, string>? clean, int maxValues, ref int count,
        ref bool changed)
    {
        byte[] text;
        if (leaf.TryGetValue(out object? held) && IsScalar(held))
        {
            text = Encoding.UTF8.GetBytes(leaf.ToJsonString(ScalarMaxDepthOptions));
        }
        else
        {
            // A new serializer starts here: count it, so a chain of leaves that lead back into each other ends at ClrReentryLimit.
            var frame = new ReentryFrame(t_reentry, maxValues, count - 1);
            if (frame.Depth > ClrReentryLimit) throw new JsonException("CLR values nest through JSON nodes too deeply");
            t_reentry = frame;
            try
            {
                using var stream = new System.IO.MemoryStream();
                using (var own = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = WalkDepth(MaxFinalValues) }))
                {
                    leaf.WriteTo(own, NodeLeafOptions.Value ?? throw new JsonException("the value cannot be serialized on this runtime"));
                }
                text = stream.ToArray();
            }
            catch (JsonException) when (frame.Exceeded)
            {
                return false;
            }
            finally
            {
                t_reentry = frame.Parent;
            }
        }

        // The value itself was counted once already.
        var status = SanitizeJson(text, clean ?? (value => value), maxValues - (count - 1), out var output, out var values, out var leafChanged);
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
    /// JSON nodes, dictionaries and sequences are rebuilt, walked with an explicit stack and no depth limit; a boolean or a finite number
    /// cannot hold text and is returned as is; every other value (a record, a Uri, a Guid, an enum) is serialized under its runtime type
    /// into a detached JSON element, so the result holds only strings, booleans, numbers, elements and the containers built here. A value
    /// with more than <see cref="MaxNodes"/> parts (a cycle included, and the
    /// values inside a JSON element or node count as parts like any others) is replaced as a whole by <see cref="OversizedValue"/> and
    /// <paramref name="oversized"/> is set: nothing is forwarded unsanitized.
    /// </summary>
    public static object? SanitizedValue(object? value, Secrets secrets) => SanitizedValue(value, secrets, out _);

    /// <summary>
    /// True when a sanitized value (what <see cref="SanitizedValue(object?, Secrets, out bool)"/> returns: dictionaries, lists, JSON
    /// elements and leaves) nests more than <paramref name="limit"/> levels, counted as a JSON parser counts them: the outermost container
    /// is level 1. Iterative, one stack entry per value, and it stops at the first value past the limit.
    /// </summary>
    internal static bool NestsDeeperThan(object? value, int limit)
    {
        var pending = new Stack<(object? Value, int Depth)>();
        pending.Push((value, 0));
        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            switch (current)
            {
                case string:
                    break;
                case JsonElement element:
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        if (depth + 1 > limit) return true;
                        foreach (var property in element.EnumerateObject()) pending.Push((property.Value, depth + 1));
                    }
                    else if (element.ValueKind == JsonValueKind.Array)
                    {
                        if (depth + 1 > limit) return true;
                        foreach (var item in element.EnumerateArray()) pending.Push((item, depth + 1));
                    }
                    break;
                case IDictionary<string, object?> map:
                    if (depth + 1 > limit) return true;
                    foreach (var item in map.Values) pending.Push((item, depth + 1));
                    break;
                case IEnumerable sequence:
                    if (depth + 1 > limit) return true;
                    foreach (var item in sequence) pending.Push((item, depth + 1));
                    break;
            }
        }
        return false;
    }

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
                // A boolean or a finite number is data and goes on as it is. Any other value (a record, a Uri, a Guid, an enum, a number
                // that is not finite, a JsonValue's CLR content) is read now, inside the boundary, into a detached JSON element: nothing
                // lazy is left to run later in a serializer the engine does not control.
                return IsScalar(value) ? value : RebuildClr(value, secrets, ref nodes);
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
        var status = SanitizeTree(json, Cleaner(secrets), MaxNodes - (nodes - 1), out var output, out var values);
        return Detach(status, output, values, ref nodes);
    }

    /// <summary>
    /// The sanitized copy of a value that is neither JSON nor a scalar: it is serialized under its runtime type (<see cref="SerializeClr"/>),
    /// so every getter runs here, and the JSON that comes out is walked and counted like any other (<see cref="RebuildJson"/>). A getter or
    /// serializer failure throws and is never replaced quietly.
    /// </summary>
    private static object? RebuildClr(object value, Secrets secrets, ref int nodes)
    {
        var status = SanitizeJson(SerializeClr(value), Cleaner(secrets), MaxNodes - (nodes - 1), out var output, out var values, out _);
        return Detach(status, output, values, ref nodes);
    }

    /// <summary>The detached element for a walked value, its values added to <paramref name="nodes"/>; over the limit it is the whole-value replacement.</summary>
    private static object? Detach(JsonWalkStatus status, byte[] output, int values, ref int nodes)
    {
        if (status == JsonWalkStatus.TooManyValues)
        {
            nodes = MaxNodes + 1;
            return OversizedValue;
        }
        if (status != JsonWalkStatus.Ok) throw new JsonException("the JSON value cannot be read");
        nodes += values - 1;
        using var doc = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = WalkDepth(MaxNodes) });
        return doc.RootElement.Clone();
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sensei.Engine.Json;
using Sensei.Engine.Text;

namespace Sensei.Engine.Tools.Schema;

/// <summary>
/// Strict validator for the checked-in tool schemas (S21, S83). Supports the JSON Schema subset the engine's
/// tools use: <c>type</c>, <c>properties</c>, <c>required</c>, <c>additionalProperties</c> (false),
/// <c>enum</c>, <c>minLength</c>/<c>maxLength</c> (Unicode scalar values), <c>pattern</c>,
/// <c>minimum</c>/<c>maximum</c>, <c>items</c>, <c>minItems</c>/<c>maxItems</c>, <c>uniqueItems</c>
/// (detail <c>duplicate_id</c>). Annotations (<c>description</c>, <c>default</c>, <c>title</c>, <c>$schema</c>)
/// are ignored. Any other keyword throws when the schema is loaded, so a schema can never validate less than it says.
/// </summary>
public sealed class ArgumentSchema
{
    /// <summary>The deepest a tool-call arguments document may nest, the arguments object itself being level 1: the parser's limit (S21), and the depth past which an outbound call's arguments are replaced by an empty object (S133).</summary>
    internal const int MaxArgumentDepth = 32;

    private static readonly HashSet<string> ValidationKeywords = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties", "enum", "minLength", "maxLength", "pattern",
        "minimum", "maximum", "items", "minItems", "maxItems", "uniqueItems",
    };

    private static readonly HashSet<string> AnnotationKeywords = new(StringComparer.Ordinal)
    {
        "description", "default", "title", "$schema", "examples",
    };

    private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);

    public ArgumentSchema(string schemaJson)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        Root = doc.RootElement.Clone();
        if (Root.ValueKind != JsonValueKind.Object) throw new ArgumentException("tool schema must be a JSON object");
        CheckSchema(Root, "#");
    }

    /// <summary>The schema as sent to the model.</summary>
    public JsonElement Root { get; }

    /// <summary>
    /// Validates <paramref name="instance"/>: duplicate property names at any depth first, then the schema.
    /// Returns null when valid, else a detail naming the offending property path.
    /// </summary>
    public string? Validate(JsonElement instance)
    {
        var dup = FindDuplicateProperty(instance, "");
        if (dup is not null) return dup;
        return ValidateNode(Root, instance, "");
    }

    /// <summary>
    /// Parses <paramref name="json"/>. Returns false on malformed JSON, including null, empty and whitespace-only text:
    /// a blank document is not a JSON object (S21), so a caller that wants no arguments sends <c>{}</c>.
    /// </summary>
    public static bool TryParse(string? json, out JsonElement element)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            element = default;
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(json!, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = MaxArgumentDepth });
            element = doc.RootElement.Clone();
            if (!AllStringsDecode(element))
            {
                element = default;
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException || ex is ArgumentException || ex is InvalidOperationException)
        {
            element = default;
            return false;
        }
    }

    /// <summary>False when a string or property name holds invalid UTF-16 (for example an escaped lone surrogate).</summary>
    private static bool AllStringsDecode(JsonElement element)
    {
        try
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in element.EnumerateObject())
                    {
                        if (!TextRules.IsWellFormedUtf16(p.Name) || !AllStringsDecode(p.Value)) return false;
                    }
                    return true;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        if (!AllStringsDecode(item)) return false;
                    return true;
                case JsonValueKind.String:
                    return TextRules.IsWellFormedUtf16(element.GetString());
                default:
                    return true;
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The first duplicate property name at any depth, as a detail, or null.</summary>
    public static string? FindDuplicateProperty(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in element.EnumerateObject())
                {
                    var childPath = Join(path, p.Name);
                    if (!seen.Add(p.Name)) return $"{childPath}: duplicate property name";
                    var inner = FindDuplicateProperty(p.Value, childPath);
                    if (inner is not null) return inner;
                }
                return null;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var inner = FindDuplicateProperty(item, $"{PathOrRoot(path)}[{i}]");
                    if (inner is not null) return inner;
                    i++;
                }
                return null;
            default:
                return null;
        }
    }

    private void CheckSchema(JsonElement schema, string where)
    {
        foreach (var kw in schema.EnumerateObject())
        {
            if (AnnotationKeywords.Contains(kw.Name)) continue;
            if (!ValidationKeywords.Contains(kw.Name))
                throw new NotSupportedException($"schema keyword '{kw.Name}' at {where} is not supported by the engine validator");
            switch (kw.Name)
            {
                case "properties":
                    foreach (var p in kw.Value.EnumerateObject()) CheckSchema(p.Value, $"{where}/properties/{p.Name}");
                    break;
                case "items":
                    CheckSchema(kw.Value, $"{where}/items");
                    break;
                case "additionalProperties":
                    if (kw.Value.ValueKind != JsonValueKind.False && kw.Value.ValueKind != JsonValueKind.True)
                        throw new NotSupportedException($"additionalProperties at {where} must be a boolean");
                    break;
                case "pattern":
                    var pattern = kw.Value.GetString()!;
                    _patterns[pattern] = new Regex(AnchorEnd(pattern), RegexOptions.CultureInvariant);
                    break;
            }
        }
    }

    private string? ValidateNode(JsonElement schema, JsonElement value, string path)
    {
        var typeError = CheckType(schema, value, path);
        if (typeError is not null) return typeError;

        if (schema.TryGetProperty("enum", out var en))
        {
            var ok = en.EnumerateArray().Any(e => JsonEquals(e, value));
            if (!ok)
            {
                var allowed = string.Join(", ", en.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText()));
                return $"{PathOrRoot(path)}: must be one of {allowed}";
            }
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                return ValidateObject(schema, value, path);
            case JsonValueKind.Array:
                return ValidateArray(schema, value, path);
            case JsonValueKind.String:
                return ValidateString(schema, value.GetString()!, path);
            case JsonValueKind.Number:
                return ValidateNumber(schema, value, path);
            default:
                return null;
        }
    }

    /// <summary>JSON Schema `$` means end of string; .NET `$` also matches before a final newline, so a trailing `$` becomes `\z`.</summary>
    private static string AnchorEnd(string pattern) =>
        pattern.EndsWith("$", StringComparison.Ordinal) && !pattern.EndsWith("\\$", StringComparison.Ordinal) ? pattern.Substring(0, pattern.Length - 1) + "\\z" : pattern;

    private string? ValidateObject(JsonElement schema, JsonElement value, string path)
    {
        schema.TryGetProperty("properties", out var props);
        var hasProps = props.ValueKind == JsonValueKind.Object;
        var allowAdditional = !schema.TryGetProperty("additionalProperties", out var ap) || ap.ValueKind == JsonValueKind.True;

        var required = RequiredNames(schema);
        foreach (var p in value.EnumerateObject())
        {
            var childPath = Join(path, p.Name);
            var known = hasProps && props.TryGetProperty(p.Name, out _);
            // S114: an explicit null for an optional property is the same as omitting it.
            if (p.Value.ValueKind == JsonValueKind.Null && !required.Contains(p.Name) && (known || allowAdditional))
                continue;
            if (hasProps && props.TryGetProperty(p.Name, out var childSchema))
            {
                var err = ValidateNode(childSchema, p.Value, childPath);
                if (err is not null) return err;
            }
            else if (!allowAdditional)
            {
                return $"{childPath}: unknown property";
            }
        }

        if (schema.TryGetProperty("required", out var req))
        {
            foreach (var r in req.EnumerateArray())
            {
                var name = r.GetString()!;
                if (!value.TryGetProperty(name, out _)) return $"{Join(path, name)}: required property is missing";
            }
        }
        return null;
    }

    /// <summary>
    /// S114: returns <paramref name="instance"/> with every explicit null of an optional (non-required) property removed,
    /// at any depth the schema describes, so typed deserialization sees it as omitted. Call after <see cref="Validate"/>.
    /// </summary>
    public JsonElement RemoveNullOptionals(JsonElement instance)
    {
        var node = JsonNode.Parse(instance.GetRawText());
        Strip(Root, node);
        return JsonCopy.ToElement(node);
    }

    private static void Strip(JsonElement schema, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var required = RequiredNames(schema);
                schema.TryGetProperty("properties", out var props);
                foreach (var name in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[name];
                    if (child is null)
                    {
                        if (!required.Contains(name)) obj.Remove(name);
                        continue;
                    }
                    if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty(name, out var childSchema)) Strip(childSchema, child);
                }
                break;
            }
            case JsonArray arr:
                if (schema.TryGetProperty("items", out var items))
                    foreach (var item in arr) Strip(items, item);
                break;
        }
    }

    private static HashSet<string> RequiredNames(JsonElement schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out var req))
            foreach (var r in req.EnumerateArray()) set.Add(r.GetString()!);
        return set;
    }

    private string? ValidateArray(JsonElement schema, JsonElement value, string path)
    {
        var count = value.GetArrayLength();
        if (schema.TryGetProperty("minItems", out var min) && count < min.GetInt32())
            return $"{PathOrRoot(path)}: must have at least {min.GetInt32()} item(s)";
        if (schema.TryGetProperty("maxItems", out var max) && count > max.GetInt32())
            return $"{PathOrRoot(path)}: must have at most {max.GetInt32()} item(s)";
        if (schema.TryGetProperty("items", out var items))
        {
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                var err = ValidateNode(items, item, $"{PathOrRoot(path)}[{i}]");
                if (err is not null) return err;
                i++;
            }
        }
        if (schema.TryGetProperty("uniqueItems", out var unique) && unique.ValueKind == JsonValueKind.True)
        {
            var seen = new List<JsonElement>();
            foreach (var item in value.EnumerateArray())
            {
                if (seen.Any(s => JsonEquals(s, item))) return ToolDetails.DuplicateId;
                seen.Add(item);
            }
        }
        return null;
    }

    private string? ValidateString(JsonElement schema, string text, string path)
    {
        var length = TextRules.ScalarLength(text);
        if (schema.TryGetProperty("minLength", out var min) && length < min.GetInt32())
            return $"{PathOrRoot(path)}: must be at least {min.GetInt32()} character(s)";
        if (schema.TryGetProperty("maxLength", out var max) && length > max.GetInt32())
            return $"{PathOrRoot(path)}: must be at most {max.GetInt32()} character(s)";
        if (schema.TryGetProperty("pattern", out var pattern) && !_patterns[pattern.GetString()!].IsMatch(text))
            return $"{PathOrRoot(path)}: must match {pattern.GetString()}";
        return null;
    }

    private static string? ValidateNumber(JsonElement schema, JsonElement value, string path)
    {
        var number = value.GetDouble();
        if (schema.TryGetProperty("minimum", out var min) && number < min.GetDouble())
            return $"{PathOrRoot(path)}: must be at least {min.GetRawText()}";
        if (schema.TryGetProperty("maximum", out var max) && number > max.GetDouble())
            return $"{PathOrRoot(path)}: must be at most {max.GetRawText()}";
        return null;
    }

    private static string? CheckType(JsonElement schema, JsonElement value, string path)
    {
        if (!schema.TryGetProperty("type", out var type)) return null;
        var types = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(t => t.GetString()!).ToArray()
            : new[] { type.GetString()! };
        if (types.Any(t => MatchesType(t, value))) return null;
        return $"{PathOrRoot(path)}: must be {string.Join(" or ", types.Select(Article))}";
    }

    private static bool MatchesType(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && IsIntegerLiteral(value),
        _ => throw new NotSupportedException($"schema type '{type}' is not supported"),
    };

    private static bool IsIntegerLiteral(JsonElement value)
    {
        var raw = value.GetRawText();
        return raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && value.TryGetInt64(out _);
    }

    private static string Article(string type) => type switch
    {
        "object" => "an object",
        "array" => "an array",
        "integer" => "an integer",
        "null" => "null",
        _ => "a " + type,
    };

    /// <summary>Structural JSON equality (object property order ignored).</summary>
    public static bool JsonEquals(JsonElement a, JsonElement b) =>
        string.Equals(CanonicalJson.Write(a), CanonicalJson.Write(b), StringComparison.Ordinal);

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static string PathOrRoot(string path) => path.Length == 0 ? "arguments" : path;
}

/// <summary>Canonical JSON text: object properties sorted ordinally, no whitespace, numbers as written (S82).</summary>
public static class CanonicalJson
{
    public static string Write(JsonElement element)
    {
        var sb = new System.Text.StringBuilder();
        Append(sb, element);
        return sb.ToString();
    }

    /// <summary>Canonical form of a JSON string, or null when it does not parse.</summary>
    public static string? TryCanonicalize(string? json)
    {
        if (!ArgumentSchema.TryParse(json, out var e)) return null;
        return Write(e);
    }

    private static void Append(System.Text.StringBuilder sb, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                var first = true;
                foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonEncodedText.Encode(p.Name).ToString().Insert(0, "\"")).Append("\":");
                    Append(sb, p.Value);
                }
                sb.Append('}');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                var firstItem = true;
                foreach (var item in e.EnumerateArray())
                {
                    if (!firstItem) sb.Append(',');
                    firstItem = false;
                    Append(sb, item);
                }
                sb.Append(']');
                break;
            case JsonValueKind.String:
                sb.Append('"').Append(JsonEncodedText.Encode(e.GetString()!).ToString()).Append('"');
                break;
            case JsonValueKind.Number:
                sb.Append(e.TryGetDecimal(out var d) ? d.ToString(CultureInfo.InvariantCulture) : e.GetRawText());
                break;
            default:
                sb.Append(e.GetRawText());
                break;
        }
    }
}

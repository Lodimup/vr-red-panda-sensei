using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

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

    /// <summary>
    /// A sanitized copy of a loosely typed value (the arguments or result of a function call in a chat message): strings, JSON elements,
    /// dictionaries and sequences are rebuilt; anything else (numbers, booleans) cannot hold text and is returned as is.
    /// </summary>
    public static object? SanitizedValue(object? value, Secrets secrets, int depth = 0)
    {
        if (value is null || depth > 32) return value;
        switch (value)
        {
            case string text:
                return secrets.Sanitize(text) ?? "";
            case JsonElement element:
                return Sanitized(element, secrets);
            case IDictionary<string, object?> map:
            {
                var copy = new Dictionary<string, object?>(map.Count, StringComparer.Ordinal);
                foreach (var pair in map) copy[secrets.Sanitize(pair.Key) ?? ""] = SanitizedValue(pair.Value, secrets, depth + 1);
                return copy;
            }
            case IEnumerable sequence:
            {
                var copy = new List<object?>();
                foreach (var item in sequence) copy.Add(SanitizedValue(item, secrets, depth + 1));
                return copy;
            }
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

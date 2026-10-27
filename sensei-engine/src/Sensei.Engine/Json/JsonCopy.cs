using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sensei.Engine.Json;

/// <summary>Deep copies and conversions through source-generated type info (S3).</summary>
public static class JsonCopy
{
    public static T Clone<T>(T value, JsonTypeInfo<T> info)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, info);
        return JsonSerializer.Deserialize(bytes, info)!;
    }

    public static TOut Convert<TIn, TOut>(TIn value, JsonTypeInfo<TIn> from, JsonTypeInfo<TOut> to)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, from);
        return JsonSerializer.Deserialize(bytes, to)!;
    }

    /// <summary>Serializes <paramref name="value"/> to a detached <see cref="JsonElement"/> (for <c>ToolResult.data</c>).</summary>
    public static JsonElement ToElement<T>(T value, JsonTypeInfo<T> info) => JsonSerializer.SerializeToElement(value, info);

    /// <summary>Serializes a JSON node to a detached element.</summary>
    public static JsonElement ToElement(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is null)
        {
            using var nullDoc = JsonDocument.Parse("null");
            return nullDoc.RootElement.Clone();
        }
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }
}

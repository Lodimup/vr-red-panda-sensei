using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Sensei.Engine.Agent;

/// <summary>
/// Keeps the conversation history of earlier turns small (s5). Every model request carries the whole history, so what
/// an earlier turn left behind is paid for again on every round of every later turn. For turns that are over:
/// a tool result shrinks to what a later turn can still use (status, error code, detail, affected ids), because the
/// data a call returned (the scene, the learner profile, a task's full view) is in the current state or can be asked
/// for again; and the assistant's plain text, which nobody ever heard, goes. The learner messages, the assistant's
/// tool calls with their arguments (what Sensei said and did) and the one-result-per-call pairing (S65, S85) stay.
/// </summary>
internal static class HistoryCompactor
{
    /// <summary>A compacted copy of <paramref name="messages"/>; the originals are not modified.</summary>
    public static List<ChatMessage> Compact(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count);
        foreach (var message in messages)
        {
            var compacted = CompactMessage(message);
            if (compacted is not null) result.Add(compacted);
        }
        return result;
    }

    private static ChatMessage? CompactMessage(ChatMessage message)
    {
        if (message.Role == ChatRole.Assistant)
        {
            var contents = message.Contents.Where(c => c is not TextContent and not TextReasoningContent).ToList();
            if (contents.Count == message.Contents.Count) return message;
            if (contents.Count == 0) return null;
            var copy = message.Clone();
            copy.Contents = contents;
            return copy;
        }

        if (message.Role == ChatRole.Tool && message.Contents.OfType<FunctionResultContent>().Any())
        {
            var copy = message.Clone();
            copy.Contents = message.Contents.Select(c => c is FunctionResultContent r ? CompactResult(r) : c).ToList();
            return copy;
        }

        return message;
    }

    private static AIContent CompactResult(FunctionResultContent result)
    {
        var element = result.Result is JsonElement e ? e : TryParse(result.Result?.ToString());
        if (element is not { ValueKind: JsonValueKind.Object } json) return result;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            Copy(json, "status", writer, skipEmptyArray: false);
            Copy(json, "error_code", writer, skipEmptyArray: false);
            Copy(json, "detail", writer, skipEmptyArray: false);
            Copy(json, "affected_ids", writer, skipEmptyArray: true);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return new FunctionResultContent(result.CallId, document.RootElement.Clone());
    }

    private static void Copy(JsonElement source, string name, Utf8JsonWriter writer, bool skipEmptyArray)
    {
        if (!source.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return;
        if (skipEmptyArray && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0) return;
        writer.WritePropertyName(name);
        value.WriteTo(writer);
    }

    private static JsonElement? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

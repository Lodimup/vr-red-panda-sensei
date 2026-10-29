using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System;
using Microsoft.Extensions.AI;
using Sensei.Engine.Trace;
using Sensei.Engine.Json;

namespace Sensei.Engine.Agent;

/// <summary>
/// Keeps the conversation history of earlier turns small (s5). Every model request carries the whole history, so what
/// an earlier turn left behind is paid for again on every round of every later turn. For turns that are over:
/// a tool result shrinks to what a later turn can still use (status, error code, detail, affected ids), because the
/// data a call returned (the scene, the learner profile, a task's full view) is in the current state or can be asked
/// for again. The learner messages, compacted tool calls and the one-result-per-call pairing (S65, S85) stay;
/// the stored history is not modified.
/// </summary>
internal static class HistoryCompactor
{
    /// <summary>Nesting accepted when a stored result is read; the engine's own results are flat, so anything deeper is left as it is.</summary>
    private const int MaxDepth = JsonRedaction.ClrNestingLimit;

    /// <summary>A compacted copy of <paramref name="messages"/>; the originals are not modified.</summary>
    public static List<ChatMessage> Compact(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count);
        var completed = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Where(r => { var e = r.Result is JsonElement j ? j : r.Result is string text ? TryParse(text) : null; return e is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() != "cancelled"; })
            .Select(r => r.CallId).ToHashSet(StringComparer.Ordinal);
        foreach (var message in messages) result.Add(CompactMessage(message, completed));
        return result;
    }

    private static ChatMessage CompactMessage(ChatMessage message, HashSet<string> completed)
    {
        if (message.Role == ChatRole.Assistant && message.Contents.OfType<FunctionCallContent>().Any())
        {
            var copy = message.Clone();
            copy.Contents = message.Contents.Select(c => c is FunctionCallContent f && completed.Contains(f.CallId) ? CompactCall(f) : c).ToList();
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

    private static AIContent CompactCall(FunctionCallContent call)
    {
        if (!new[] { "speak", "show_hint", "start_challenge", "update_challenge", "record_attempt", "spawn_objects", "move_object", "remove_objects", "highlight_objects", "set_support", "inspect_scene", "animate_sensei", "query_learner", "remember_note", "end_activity", "replay_speech" }.Contains(call.Name)) return call;
        JsonObject? args;
        try { args = JsonSerializer.SerializeToNode(call.Arguments, SenseiJsonContext.Default.DictionaryStringObject) as JsonObject; }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException) { return call; }
        if (args is null) return call;
        var keep = call.Name switch
        {
            "speak" => new[] { "segments" },
            "show_hint" => new[] { "content" },
            "start_challenge" or "update_challenge" => new[] { "family", "action", "instruction", "skill_targets" },
            "record_attempt" => new[] { "target", "communicative_success", "language_assessment", "uncertainty", "is_fresh_variation" },
            "spawn_objects" => new[] { "objects" },
            "move_object" => new[] { "object_id", "destination" },
            "remove_objects" or "highlight_objects" => new[] { "object_ids" },
            "set_support" => new[] { "show_japanese", "show_reading", "show_romaji", "show_translation", "pace", "correction_style", "language_mode", "difficulty", "starting_band", "self_reported_jlpt" },
            _ => Array.Empty<string>()
        };
        foreach (var key in args.Select(x => x.Key).ToArray()) if (!keep.Contains(key)) args.Remove(key);
        if (args["segments"] is JsonArray segments)
            foreach (var segment in segments.OfType<JsonObject>()) KeepOnly(segment, "lang", "text");
        if (args["content"] is JsonObject content) KeepOnly(content, "japanese", "english");
        if (args["skill_targets"] is JsonArray targets)
            foreach (var target in targets.OfType<JsonObject>()) KeepOnly(target, "key");
        if (args["target"] is JsonObject attempt) KeepOnly(attempt, "key");
        if (args["objects"] is JsonArray objects)
            foreach (var obj in objects.OfType<JsonObject>()) KeepOnly(obj, "object_id", "catalog_id", "region", "container_id");
        if (args["destination"] is JsonObject destination) KeepOnly(destination, "region", "container_id");
        var filtered = JsonSerializer.Deserialize(args.ToJsonString(new JsonSerializerOptions { MaxDepth = MaxDepth }), SenseiJsonContext.Default.DictionaryStringObject);
        return new FunctionCallContent(call.CallId, call.Name, filtered) { InformationalOnly = call.InformationalOnly };
    }

    private static void KeepOnly(JsonObject obj, params string[] keys)
    {
        foreach (var key in obj.Select(x => x.Key).ToArray()) if (!keys.Contains(key)) obj.Remove(key);
    }

    private static AIContent CompactResult(FunctionResultContent result)
    {
        // Only a result the engine stored (a JSON element or its text) is read; any other object is never serialized here.
        var element = result.Result switch { JsonElement e => e, string text => TryParse(text), _ => (JsonElement?)null };
        // Only the engine's own tool results (they all carry a status) are shrunk; any other shape goes back exactly as it was.
        if (element is not { ValueKind: JsonValueKind.Object } json || !json.TryGetProperty("status", out _)) return result;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = MaxDepth + 2 }))
        {
            writer.WriteStartObject();
            Copy(json, "status", writer, skipEmptyArray: false);
            Copy(json, "error_code", writer, skipEmptyArray: false);
            Copy(json, "detail", writer, skipEmptyArray: false);
            Copy(json, "affected_ids", writer, skipEmptyArray: true);
            writer.WriteEndObject();
        }
        if (result.Result is string) return new FunctionResultContent(result.CallId, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        using var document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = MaxDepth + 2 });
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
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = MaxDepth });
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

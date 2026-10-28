using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Agent;

/// <summary>
/// S65, PRD §10: before each turn, every function call in the history that has no result gets a synthetic
/// <c>{"status":"cancelled","error_code":"cancelled"}</c> result, so the provider never receives an unanswered tool call.
/// </summary>
internal static class HistorySettler
{
    private const string CancelledResult = "{\"status\":\"cancelled\",\"error_code\":\"cancelled\"}";

    /// <summary>Settles <paramref name="messages"/> in place; returns how many synthetic results were added.</summary>
    public static int Settle(List<ChatMessage> messages)
    {
        var added = 0;
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (message.Role != ChatRole.Assistant) continue;
            var calls = message.Contents.OfType<FunctionCallContent>().ToList();
            if (calls.Count == 0) continue;

            // Results for this message's calls sit in the tool messages that follow it directly.
            var next = i + 1;
            var answered = new HashSet<string>(System.StringComparer.Ordinal);
            while (next < messages.Count && messages[next].Role == ChatRole.Tool)
            {
                foreach (var result in messages[next].Contents.OfType<FunctionResultContent>()) answered.Add(result.CallId);
                next++;
            }

            var missing = calls.Where(c => !answered.Contains(c.CallId)).ToList();
            if (missing.Count == 0) continue;

            var synthetic = new ChatMessage(ChatRole.Tool, missing.Select(c => (AIContent)new FunctionResultContent(c.CallId, CancelledElement())).ToList());
            messages.Insert(next, synthetic);
            added += missing.Count;
            i = next;
        }
        return added;
    }

    private static JsonElement CancelledElement()
    {
        using var doc = JsonDocument.Parse(CancelledResult, new JsonDocumentOptions { MaxDepth = JsonRedaction.WalkDepth(JsonRedaction.MaxFinalValues) });
        return doc.RootElement.Clone();
    }
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sensei.Engine.Agent;

/// <summary>
/// S85: every provider function call goes through the engine pipeline, including one the provider named with a blank name. The
/// chat-completions adapter turns a tool call with an empty or whitespace name into nothing at all, so the call would vanish before
/// the engine could answer it and the provider would be left with an unanswered call id. The adapter keeps the provider's own
/// answer as the response's raw representation; the calls it dropped are read from there and added back as ordinary function calls,
/// under their own call id and name, so the pipeline answers them <c>unknown_tool</c> like any other unknown name.
/// </summary>
internal static class WireCallRecovery
{
    public static void Recover(ChatResponse response)
    {
        if (response.RawRepresentation is not ChatCompletion completion) return;
        var dropped = completion.ToolCalls
            .Where(c => c.Kind == ChatToolCallKind.Function && string.IsNullOrWhiteSpace(c.FunctionName))
            .ToList();
        if (dropped.Count == 0) return;

        var known = new HashSet<string>(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.CallId));
        var message = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        if (message is null)
        {
            message = new ChatMessage(ChatRole.Assistant, new List<AIContent>());
            response.Messages.Add(message);
        }
        foreach (var call in dropped)
        {
            if (known.Contains(call.Id)) continue;
            message.Contents.Add(new FunctionCallContent(call.Id, call.FunctionName ?? "", null) { RawRepresentation = call });
        }
    }
}

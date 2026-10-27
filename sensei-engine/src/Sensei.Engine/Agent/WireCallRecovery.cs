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
/// <remarks>
/// S61, S25: calls from one response run serially in the order the provider gave them, and the second validation failure of a turn
/// exhausts it. A recovered call therefore goes back to the position it had in the provider's list, between the calls the adapter
/// kept; appending it after them would let a later valid call run before the failures that should have exhausted the turn.
/// </remarks>
internal static class WireCallRecovery
{
    public static void Recover(ChatResponse response)
    {
        if (response.RawRepresentation is not ChatCompletion completion) return;
        var wire = completion.ToolCalls.Where(c => c.Kind == ChatToolCallKind.Function).ToList();
        if (!wire.Any(IsBlank)) return;

        var known = new HashSet<string>(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.CallId));
        var message = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        if (message is null)
        {
            message = new ChatMessage(ChatRole.Assistant, new List<AIContent>());
            response.Messages.Add(message);
        }

        // The calls the adapter kept, in the order it kept them, and where the first of them stands among the message's contents.
        var survivors = message.Contents.OfType<FunctionCallContent>().ToList();
        var position = survivors.Count > 0 ? message.Contents.IndexOf(survivors[0]) : message.Contents.Count;

        // The provider's order: a kept call takes the place of its wire call (matched by id, first unused one), a blank one is rebuilt.
        var ordered = new List<FunctionCallContent>();
        var unused = new List<FunctionCallContent>(survivors);
        foreach (var call in wire)
        {
            if (IsBlank(call))
            {
                if (!known.Contains(call.Id)) ordered.Add(new FunctionCallContent(call.Id, call.FunctionName ?? "", null) { RawRepresentation = call });
                continue;
            }
            var kept = unused.FindIndex(c => c.CallId == call.Id);
            if (kept < 0) continue;
            ordered.Add(unused[kept]);
            unused.RemoveAt(kept);
        }
        // Anything the adapter kept that no wire call accounts for stays, after the others, in its own order.
        ordered.AddRange(unused);

        foreach (var kept in survivors) message.Contents.Remove(kept);
        for (var i = 0; i < ordered.Count; i++) message.Contents.Insert(position + i, ordered[i]);
    }

    private static bool IsBlank(ChatToolCall call) => string.IsNullOrWhiteSpace(call.FunctionName);
}

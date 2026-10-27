using System;
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
/// <para>
/// S61, S25: calls from one response run serially in the order the provider gave them, and the second validation failure of a turn
/// exhausts it. A recovered call therefore goes back to the position it had in the provider's list, between the calls the adapter
/// kept; appending it after them would let a later valid call run before the failures that should have exhausted the turn.
/// </para>
/// <para>
/// Which call the adapter kept is decided by identity, never by call id: each function call it builds carries the wire call it came from
/// as its raw representation. A provider may reuse one id for several calls (S82); matching by id would take a blank call for a kept one
/// that merely shares its id, drop it, and let the kept call run without the validation failure the blank one owes the turn's budget.
/// Every wire call therefore appears once, in the provider's order.
/// </para>
/// <para>
/// A response that needed recovery goes back to the provider with every call id distinct (a strict provider refuses a request with two
/// calls of one id). The first call with an id keeps it; a later call with the same id is given an id of the engine's own (<c>auto-N</c>).
/// The pipeline reserves that prefix for the engine (S82), so the renamed call is answered <c>invalid_arguments</c> and never executed:
/// a repeated id cannot be used to run a second, different call under the first one's identity.
/// </para>
/// </remarks>
internal static class WireCallRecovery
{
    /// <param name="response">The adapter's response; changed in place when a call was dropped from it.</param>
    /// <param name="engineCallId">Hands out a new id of the engine's own, unique in the engine instance.</param>
    public static void Recover(ChatResponse response, Func<string> engineCallId)
    {
        if (response.RawRepresentation is not ChatCompletion completion) return;
        var wire = completion.ToolCalls.Where(c => c.Kind == ChatToolCallKind.Function).ToList();
        if (!wire.Any(IsBlank)) return;

        var message = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        if (message is null)
        {
            message = new ChatMessage(ChatRole.Assistant, new List<AIContent>());
            response.Messages.Add(message);
        }

        // The calls the adapter kept, in the order it kept them, and where the first of them stands among the message's contents.
        var survivors = message.Contents.OfType<FunctionCallContent>().ToList();
        var position = survivors.Count > 0 ? message.Contents.IndexOf(survivors[0]) : message.Contents.Count;

        // The provider's order: a kept call takes the place of the very wire call it was built from, a blank one is rebuilt.
        var ordered = new List<FunctionCallContent>();
        var unused = new List<FunctionCallContent>(survivors);
        foreach (var call in wire)
        {
            var kept = unused.FindIndex(c => ReferenceEquals(c.RawRepresentation, call));
            if (kept < 0 && !IsBlank(call))
                kept = unused.FindIndex(c => c.RawRepresentation is not ChatToolCall && c.CallId == call.Id && c.Name == call.FunctionName);
            if (kept >= 0)
            {
                ordered.Add(unused[kept]);
                unused.RemoveAt(kept);
            }
            else if (IsBlank(call))
            {
                ordered.Add(new FunctionCallContent(call.Id, call.FunctionName ?? "", null) { RawRepresentation = call });
            }
        }
        // Anything the adapter kept that no wire call accounts for stays, after the others, in its own order.
        ordered.AddRange(unused);

        MakeIdsDistinct(ordered, engineCallId);

        foreach (var kept in survivors) message.Contents.Remove(kept);
        for (var i = 0; i < ordered.Count; i++) message.Contents.Insert(position + i, ordered[i]);
    }

    /// <summary>The first call with an id keeps it; every later call with that id becomes the same call under an id of the engine's own.</summary>
    private static void MakeIdsDistinct(List<FunctionCallContent> calls, Func<string> engineCallId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];
            if (string.IsNullOrEmpty(call.CallId) || seen.Add(call.CallId)) continue;
            calls[i] = new FunctionCallContent(engineCallId(), call.Name, call.Arguments)
            {
                Exception = call.Exception,
                InformationalOnly = call.InformationalOnly,
                RawRepresentation = call.RawRepresentation,
            };
        }
    }

    private static bool IsBlank(ChatToolCall call) => string.IsNullOrWhiteSpace(call.FunctionName);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.AI;
using Sensei.Engine.Text;

namespace Sensei.Engine.Trace;

/// <summary>
/// S133: what happens to a call id on its way to the provider. A call id is a protocol identifier: the provider pairs a call with its
/// result by it and refuses a request whose ids are not the ones it issued, so the credential pattern (S78) is never applied to one (a live
/// provider's ids are 32 or more letters and digits, which the pattern matches). A known secret (S126) still is removed: an id that holds
/// one is replaced by an id of the engine's own, the same original id by the same replacement everywhere in one request (a call and its
/// result stay paired), different original ids by different replacements, none equal to an id already in the request. One instance is
/// one request.
/// </summary>
internal sealed class CallIdRewriter
{
    /// <summary>The prefix of the ids the engine hands out itself; the tool pipeline reserves it (S82).</summary>
    private const string EnginePrefix = "auto-";

    private readonly Secrets _secrets;
    private readonly Func<string>? _engineCallId;
    private readonly Func<IEnumerable<string>> _existing;
    private readonly Dictionary<string, string> _replacements = new(StringComparer.Ordinal);
    private HashSet<string>? _taken;
    private int _local;

    /// <param name="secrets">The known secrets an id may not hold.</param>
    /// <param name="engineCallId">Hands out a new id of the engine's own, unique in the engine instance; null when there is no engine (the rewriter then counts for itself).</param>
    /// <param name="existingIds">Every call id of the request, read once, when the first replacement is needed; a replacement is never one of these.</param>
    public CallIdRewriter(Secrets secrets, Func<string>? engineCallId, Func<IEnumerable<string>> existingIds)
    {
        _secrets = secrets;
        _engineCallId = engineCallId;
        _existing = existingIds;
    }

    /// <summary>The call ids of the function calls and function results in <paramref name="messages"/>.</summary>
    public static IEnumerable<string> IdsIn(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent call) yield return call.CallId;
                else if (content is FunctionResultContent result) yield return result.CallId;
            }
        }
    }

    /// <summary>The id that may be sent for <paramref name="id"/>: as it is, with lone surrogates replaced like any text, unless it holds a known secret.</summary>
    public string Clean(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var text = TextRules.ReplaceLoneSurrogates(id!);
        if (!_secrets.ContainsKnown(text)) return text;
        if (_replacements.TryGetValue(text, out var replacement)) return replacement;
        replacement = Fresh();
        _replacements[text] = replacement;
        return replacement;
    }

    private string Fresh()
    {
        _taken ??= new HashSet<string>(_existing(), StringComparer.Ordinal);
        while (true)
        {
            var candidate = _engineCallId is { } next ? next() : EnginePrefix + (++_local).ToString(CultureInfo.InvariantCulture);
            if (_taken.Add(candidate)) return candidate;
        }
    }
}

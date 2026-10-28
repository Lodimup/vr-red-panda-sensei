using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Sensei.Engine.Text;

namespace Sensei.Engine.Trace;

/// <summary>The credential pattern of S78 (replaces S56). Case-insensitive.</summary>
public static class CredentialPattern
{
    public const string Redacted = "[redacted]";

    private static readonly Regex SkKey = new(@"sk-[a-z0-9_-]{16,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Assignment = new(@"(api[_-]?key|password|passwd|token|secret)\s*[:=]\s*\S{6,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LongRun = new(@"[A-Za-z0-9+/_=-]{32,}", RegexOptions.CultureInvariant);

    /// <summary>True when <paramref name="text"/> contains a credential-pattern match.</summary>
    public static bool IsMatch(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (SkKey.IsMatch(text) || Assignment.IsMatch(text)) return true;
        foreach (Match m in LongRun.Matches(text)) if (IsMixedRun(m.Value)) return true;
        return false;
    }

    /// <summary>
    /// Replaces every credential-pattern match with <c>[redacted]</c>. Every pattern is matched against the original text and
    /// the overlapping spans are redacted as one, so one match never splits another (S78).
    /// </summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var spans = new List<Span>();
        AddMatches(text, spans);
        return Redact(text, spans);
    }

    /// <summary>Adds the span of every credential-pattern match in <paramref name="text"/>.</summary>
    internal static void AddMatches(string text, List<Span> spans)
    {
        foreach (Match m in SkKey.Matches(text)) spans.Add(new Span(m.Index, m.Index + m.Length));
        foreach (Match m in Assignment.Matches(text)) spans.Add(new Span(m.Index, m.Index + m.Length));
        foreach (Match m in LongRun.Matches(text))
            if (IsMixedRun(m.Value)) spans.Add(new Span(m.Index, m.Index + m.Length));
    }

    /// <summary>Replaces the union of <paramref name="spans"/> (overlapping or touching spans merge) with <c>[redacted]</c>.</summary>
    internal static string Redact(string text, List<Span> spans)
    {
        if (spans.Count == 0) return text;
        spans.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
        var result = new StringBuilder(text.Length);
        var position = 0;
        var start = spans[0].Start;
        var end = spans[0].End;
        for (var i = 1; i <= spans.Count; i++)
        {
            if (i < spans.Count && spans[i].Start <= end)
            {
                if (spans[i].End > end) end = spans[i].End;
                continue;
            }
            result.Append(text, position, start - position).Append(Redacted);
            position = end;
            if (i < spans.Count)
            {
                start = spans[i].Start;
                end = spans[i].End;
            }
        }
        result.Append(text, position, text.Length - position);
        return result.ToString();
    }

    private static bool IsMixedRun(string run) => run.Any(char.IsLetter) && run.Any(char.IsDigit);

    /// <summary>A half-open character range <c>[Start, End)</c> of a text to redact.</summary>
    internal readonly record struct Span(int Start, int End);
}

/// <summary>
/// Known secret values plus the credential pattern (S77). <see cref="Check"/> guards persisted learner text;
/// <see cref="Sanitize"/> cleans every string written to a trace, the console, an exception or a tool result.
/// </summary>
public sealed class Secrets
{
    /// <summary>S126: shorter exact values are never searched for inside other text; they would mangle ordinary words and model names.</summary>
    public const int MinimumKnownSecretLength = 8;

    private readonly string[] _known;

    public Secrets(IEnumerable<string>? knownSecrets)
    {
        _known = (knownSecrets ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrEmpty(s) && s.Length >= MinimumKnownSecretLength)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(s => s.Length)
            .ToArray();
    }

    /// <summary>No known secrets; the credential pattern still applies.</summary>
    public static Secrets None { get; } = new(null);

    /// <summary>
    /// A set that knows every secret of this one and of <paramref name="other"/> (each value once). Used by the outbound byte guard, which
    /// starts with the client's key and learns the engine's known secrets when an engine attaches to it.
    /// </summary>
    internal Secrets Union(Secrets other) => new(_known.Concat(other._known));

    /// <summary>True when <paramref name="text"/> contains a known secret (S126, exact match). The credential pattern is not asked.</summary>
    internal bool ContainsKnown(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var s in _known)
            if (text!.IndexOf(s, StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    /// <summary>True when <paramref name="text"/> contains a known secret or a credential-pattern match (→ <c>looks_like_credential</c>).</summary>
    public bool Check(string? text) => ContainsKnown(text) || CredentialPattern.IsMatch(text);

    /// <summary>
    /// Known secrets and credential-pattern matches replaced by <c>[redacted]</c>; null stays null. Known secrets and patterns
    /// are all matched against the original text and their overlapping spans redacted as one, so a replaced secret can never
    /// split a longer credential into fragments that no longer match.
    /// </summary>
    public string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var clean = TextRules.ReplaceLoneSurrogates(text!);
        var spans = new List<CredentialPattern.Span>();
        foreach (var s in _known)
        {
            for (var at = clean.IndexOf(s, StringComparison.Ordinal); at >= 0; at = clean.IndexOf(s, at + 1, StringComparison.Ordinal))
                spans.Add(new CredentialPattern.Span(at, at + s.Length));
        }
        CredentialPattern.AddMatches(clean, spans);
        return CredentialPattern.Redact(clean, spans);
    }
}

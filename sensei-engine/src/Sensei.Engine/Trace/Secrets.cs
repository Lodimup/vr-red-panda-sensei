using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

    /// <summary>Replaces every credential-pattern match with <c>[redacted]</c>.</summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = SkKey.Replace(text, Redacted);
        result = Assignment.Replace(result, Redacted);
        result = LongRun.Replace(result, m => IsMixedRun(m.Value) ? Redacted : m.Value);
        return result;
    }

    private static bool IsMixedRun(string run) => run.Any(char.IsLetter) && run.Any(char.IsDigit);
}

/// <summary>
/// Known secret values plus the credential pattern (S77). <see cref="Check"/> guards persisted learner text;
/// <see cref="Sanitize"/> cleans every string written to a trace, the console, an exception or a tool result.
/// </summary>
public sealed class Secrets
{
    private readonly string[] _known;

    public Secrets(IEnumerable<string>? knownSecrets)
    {
        _known = (knownSecrets ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(s => s.Length)
            .ToArray();
    }

    /// <summary>No known secrets; the credential pattern still applies.</summary>
    public static Secrets None { get; } = new(null);

    /// <summary>True when <paramref name="text"/> contains a known secret or a credential-pattern match (→ <c>looks_like_credential</c>).</summary>
    public bool Check(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var s in _known)
            if (text!.IndexOf(s, StringComparison.Ordinal) >= 0) return true;
        return CredentialPattern.IsMatch(text);
    }

    /// <summary>Known secrets and credential-pattern matches replaced by <c>[redacted]</c>; null stays null.</summary>
    public string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = text!;
        foreach (var s in _known)
            result = result.Replace(s, CredentialPattern.Redacted);
        return CredentialPattern.Redact(result);
    }
}

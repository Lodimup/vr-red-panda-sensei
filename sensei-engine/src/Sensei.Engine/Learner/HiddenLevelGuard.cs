using Sensei.Engine.Text;
using System.Text.RegularExpressions;

namespace Sensei.Engine.Learner;

/// <summary>
/// S80: model-written presentation text (<c>speak</c> text, <c>show_hint</c> content, <c>next_suggestion</c>) must not
/// mention the hidden working level. RULING(Handler 2026-10-27): hidden JLPT working level is never shown to the learner.
/// </summary>
public static class HiddenLevelGuard
{
    /// <summary>The detail of the <c>invalid_arguments</c> failure.</summary>
    public const string Detail = "hidden_level";

    private static readonly Regex Person = new(@"\b(?:you|your|you're|youre|ur|u|yours)\b|あなた|きみ|君", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Level = new(@"\bjlpt\b|pre[_ -]?n[1-5]|(?<![a-zA-Z0-9_])[nｎＮ][1-5](?![a-zA-Z0-9_])|レベル", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // RULING(Handler 2026-10-28): preserve the existing learner-level phrases alongside the two-part rule.
    private static readonly Regex InternalLevel = new(@"pre_n[1-5]|\byour\s+(?:jlpt|japanese)\s+level\b|\byour\s+level\s+is\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>True when presentation text contains a prohibited level reference.</summary>
    public static bool Check(string? text) =>
        text is not null && (TextRules.ContainsIgnoreCase(text, "working level") || TextRules.ContainsIgnoreCase(text, "working_level") ||
        InternalLevel.IsMatch(text) || (Person.IsMatch(text) && Level.IsMatch(text)));
}

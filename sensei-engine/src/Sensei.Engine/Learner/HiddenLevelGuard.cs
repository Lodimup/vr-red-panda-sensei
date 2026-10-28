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

    private static readonly Regex LevelTerms = new(@"\bjlpt\b|pre[_ -]?n[1-5]|\b[nｎ][1-5]\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>True when presentation text contains a prohibited level reference.</summary>
    public static bool Check(string? text) =>
        TextRules.ContainsIgnoreCase(text, "working level") || TextRules.ContainsIgnoreCase(text, "working_level") ||
        (text is not null && LevelTerms.IsMatch(text));
}

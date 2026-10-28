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

    // Refuses text that tells the learner their own level; general talk about JLPT word levels stays allowed.
    private static readonly Regex LevelTerms = new(
        @"\byour\s+(?:jlpt|japanese)\s+level\b|\byour\s+level\s+is\b" +
        @"|\byour\s+(?:jlpt\s+)?(?:pre[_ -]?)?[nｎＮ][1-5]\b" +
        @"|\byou(?:'re|\s+are)\s+(?:at\s+|an?\s+)?(?:jlpt\s+)?(?:pre[_ -]?)?[nｎＮ][1-5]\b" +
        @"|\bpre_n[1-5]\b" +
        @"|あなたのレベル|(?:あなた|きみ|君)は\s*(?:jlpt\s*)?[nｎＮ][1-5]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>True when presentation text contains a prohibited level reference.</summary>
    public static bool Check(string? text) =>
        TextRules.ContainsIgnoreCase(text, "working level") || TextRules.ContainsIgnoreCase(text, "working_level") ||
        (text is not null && LevelTerms.IsMatch(text));
}

using System.Text;
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

    // RULING(Handler 2026-10-28): preserve the existing learner-level phrases alongside the two-part rule.
    private static readonly Regex InternalLevel = new(@"working level|working_level|pre_n[1-5]|\byour\s+(?:(?:jlpt|japanese)\s+)?level\s+is\b|\byour\s+(?:jlpt|japanese)\s+level\b", RegexOptions.CultureInvariant);
    private static readonly Regex LearnerLevel = new(@"\b(?:you(?:'re| are)|ur|u r)\s+(?:at\s+)?(?:the\s+)?(?:jlpt\s+)?(?:level\s+)?(?:pre[- ]?)?n[- ]?(?:[1-5]|one|two|three|four|five)\b|\byour\s+(?:jlpt\s+)?(?:japanese\s+)?(?:ability|level)\s+(?:is|=)\s+(?:jlpt\s+)?(?:pre[- ]?)?n[- ]?(?:[1-5]|one|two|three|four|five)\b|あなたのレベル|(?:あなた|きみ|君)(?:は|の日本語は)\s*(?:jlpt\s*)?n[1-5]", RegexOptions.CultureInvariant);

    /// <summary>True when presentation text contains a prohibited level reference.</summary>
    public static bool Check(string? text)
    {
        if (text is null) return false;
        var normalized = Regex.Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"\s+", " ");
        return InternalLevel.IsMatch(normalized) || LearnerLevel.IsMatch(normalized);
    }

}

using System;

namespace Sensei.Engine.Learner;

/// <summary>S141: transcript-derived tools must not make pronunciation claims.</summary>
internal static class PronunciationClaimGuard
{
    public const string Detail = "pronunciation_claim";
    private static readonly string[] Terms = { "pronunc", "accent", "intonation", "pitch", "発音", "アクセント", "イントネーション", "ピッチ" };

    public static bool Check(params string?[] values)
    {
        foreach (var value in values)
            if (value is not null)
                foreach (var term in Terms)
                    if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}

using System;
using System.Globalization;
using System.Text;

namespace Sensei.Engine.Text;

/// <summary>Unicode-aware text helpers shared by every slice (lengths, normalization, script detection).</summary>
public static class TextRules
{
    /// <summary>Length in Unicode scalar values (a surrogate pair counts once; combining marks count separately).</summary>
    public static int ScalarLength(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 0;
        for (var i = 0; i < text!.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            count++;
        }
        return count;
    }

    /// <summary>NFKC, lowercase (invariant) and runs of whitespace collapsed to one space, trimmed (S43).</summary>
    public static string NormalizeForMatch(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var nfkc = text!.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var sb = new StringBuilder(nfkc.Length);
        var pendingSpace = false;
        foreach (var c in nfkc)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) sb.Append(' ');
            pendingSpace = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>NFKC with all whitespace removed (S37e).</summary>
    public static string NormalizeNoWhitespace(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var nfkc = text!.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(nfkc.Length);
        foreach (var c in nfkc)
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>True when the text contains at least one hiragana, katakana or CJK ideograph.</summary>
    public static bool ContainsJapanese(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (var i = 0; i < text!.Length; i++)
        {
            int cp;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else
            {
                cp = text[i];
            }
            if (IsJapaneseCodePoint(cp)) return true;
        }
        return false;
    }

    /// <summary>Hiragana, katakana (incl. extensions and half-width) or a CJK ideograph.</summary>
    public static bool IsJapaneseCodePoint(int cp) =>
        (cp >= 0x3040 && cp <= 0x309F) ||   // hiragana
        (cp >= 0x30A0 && cp <= 0x30FF) ||   // katakana
        (cp >= 0x31F0 && cp <= 0x31FF) ||   // katakana phonetic extensions
        (cp >= 0xFF66 && cp <= 0xFF9F) ||   // half-width katakana
        (cp >= 0x3400 && cp <= 0x4DBF) ||   // CJK extension A
        (cp >= 0x4E00 && cp <= 0x9FFF) ||   // CJK unified ideographs
        (cp >= 0xF900 && cp <= 0xFAFF) ||   // CJK compatibility ideographs
        (cp >= 0x20000 && cp <= 0x323AF) || // CJK extensions B..H
        cp == 0x3005 || cp == 0x3006 || cp == 0x3007; // 々 〆 〇

    /// <summary>False when <paramref name="text"/> contains an unpaired surrogate.</summary>
    public static bool IsWellFormedUtf16(string? text)
    {
        if (text is null) return true;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Replaces every unpaired surrogate with U+FFFD so the text can be written as JSON.</summary>
    public static string ReplaceLoneSurrogates(string text)
    {
        if (IsWellFormedUtf16(text)) return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                sb.Append('\uFFFD');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Invariant ordinal-ignore-case containment.</summary>
    public static bool ContainsIgnoreCase(string? text, string value) =>
        text is not null && CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, value, CompareOptions.OrdinalIgnoreCase) >= 0;
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Sensei.Engine.Learner;

/// <summary>Support settings (SPEC §9). Defaults are the <c>zero</c> band row, pace normal, light corrections.</summary>
public sealed class SupportSettings
{
    [JsonRequired]
    public bool ShowJapanese { get; set; } = true;

    [JsonRequired]
    public bool ShowReading { get; set; } = true;

    [JsonRequired]
    public bool ShowRomaji { get; set; } = true;

    [JsonRequired]
    public bool ShowTranslation { get; set; } = true;

    [JsonRequired]
    public string Pace { get; set; } = SupportValues.PaceNormal;

    [JsonRequired]
    public string CorrectionStyle { get; set; } = SupportValues.CorrectionLight;

    [JsonRequired]
    public string LanguageMode { get; set; } = SupportValues.LanguageEnglishExplanations;

    public SupportSettings Clone() => (SupportSettings)MemberwiseClone();

    /// <summary>The value of a field by its snake_case name, as a string (<c>true</c>/<c>false</c> for booleans).</summary>
    public string GetFieldValue(string field) => field switch
    {
        SupportFields.ShowJapanese => Bool(ShowJapanese),
        SupportFields.ShowReading => Bool(ShowReading),
        SupportFields.ShowRomaji => Bool(ShowRomaji),
        SupportFields.ShowTranslation => Bool(ShowTranslation),
        SupportFields.Pace => Pace,
        SupportFields.CorrectionStyle => CorrectionStyle,
        SupportFields.LanguageMode => LanguageMode,
        _ => throw new ArgumentException($"unknown support field '{field}'", nameof(field)),
    };

    private static string Bool(bool b) => b ? "true" : "false";
}

/// <summary>A partial change to <see cref="SupportSettings"/>; null fields are left alone.</summary>
public sealed class SupportPatch
{
    public bool? ShowJapanese { get; set; }
    public bool? ShowReading { get; set; }
    public bool? ShowRomaji { get; set; }
    public bool? ShowTranslation { get; set; }
    public string? Pace { get; set; }
    public string? CorrectionStyle { get; set; }
    public string? LanguageMode { get; set; }

    /// <summary>Snake_case names of the fields this patch sets, in SPEC order.</summary>
    public IReadOnlyList<string> FieldNames()
    {
        var names = new List<string>();
        if (ShowJapanese.HasValue) names.Add(SupportFields.ShowJapanese);
        if (ShowReading.HasValue) names.Add(SupportFields.ShowReading);
        if (ShowRomaji.HasValue) names.Add(SupportFields.ShowRomaji);
        if (ShowTranslation.HasValue) names.Add(SupportFields.ShowTranslation);
        if (Pace is not null) names.Add(SupportFields.Pace);
        if (CorrectionStyle is not null) names.Add(SupportFields.CorrectionStyle);
        if (LanguageMode is not null) names.Add(SupportFields.LanguageMode);
        return names;
    }

    /// <summary>Null when every value is allowed, else a detail naming the field and allowed values.</summary>
    public string? Validate()
    {
        if (Pace is not null && !SupportValues.Paces.Contains(Pace, StringComparer.Ordinal))
            return $"pace: must be one of {string.Join(", ", SupportValues.Paces)}";
        if (CorrectionStyle is not null && !SupportValues.CorrectionStyles.Contains(CorrectionStyle, StringComparer.Ordinal))
            return $"correction_style: must be one of {string.Join(", ", SupportValues.CorrectionStyles)}";
        if (LanguageMode is not null && !SupportValues.LanguageModes.Contains(LanguageMode, StringComparer.Ordinal))
            return $"language_mode: must be one of {string.Join(", ", SupportValues.LanguageModes)}";
        return null;
    }
}

/// <summary>Snake_case support field names, as used in <c>pinned</c> and <c>preference_changes</c>.</summary>
public static class SupportFields
{
    public const string ShowJapanese = "show_japanese";
    public const string ShowReading = "show_reading";
    public const string ShowRomaji = "show_romaji";
    public const string ShowTranslation = "show_translation";
    public const string Pace = "pace";
    public const string CorrectionStyle = "correction_style";
    public const string LanguageMode = "language_mode";

    /// <summary>Not a support field, but recorded in <c>preference_changes</c> when the band changes.</summary>
    public const string StartingBand = "starting_band";

    public static IReadOnlyList<string> All { get; } = new[] { ShowJapanese, ShowReading, ShowRomaji, ShowTranslation, Pace, CorrectionStyle, LanguageMode };

    /// <summary>The four caption display fields a band row sets.</summary>
    public static IReadOnlyList<string> Display { get; } = new[] { ShowJapanese, ShowReading, ShowRomaji, ShowTranslation };
}

/// <summary>Allowed values of the string support fields.</summary>
public static class SupportValues
{
    public const string PaceNormal = "normal";
    public const string PaceSlow = "slow";
    public const string CorrectionLight = "light";
    public const string CorrectionDetailed = "detailed";
    public const string LanguageEnglishExplanations = "english_explanations";
    public const string LanguageMixed = "mixed";
    public const string LanguageJapaneseOnly = "japanese_only";

    public static IReadOnlyList<string> Paces { get; } = new[] { PaceNormal, PaceSlow };
    public static IReadOnlyList<string> CorrectionStyles { get; } = new[] { CorrectionLight, CorrectionDetailed };
    public static IReadOnlyList<string> LanguageModes { get; } = new[] { LanguageEnglishExplanations, LanguageMixed, LanguageJapaneseOnly };
}

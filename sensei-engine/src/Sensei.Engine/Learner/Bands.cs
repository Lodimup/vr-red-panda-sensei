using System;
using System.Collections.Generic;
using System.Linq;

namespace Sensei.Engine.Learner;

/// <summary>One row of the starting-band table (SPEC §9).</summary>
public sealed class BandRow
{
    public BandRow(string band, bool showJapanese, bool showReading, bool showRomaji, bool showTranslation, string languageMode, string initialWorkingLevel)
    {
        Band = band;
        ShowJapanese = showJapanese;
        ShowReading = showReading;
        ShowRomaji = showRomaji;
        ShowTranslation = showTranslation;
        LanguageMode = languageMode;
        InitialWorkingLevel = initialWorkingLevel;
    }

    public string Band { get; }
    public bool ShowJapanese { get; }
    public bool ShowReading { get; }
    public bool ShowRomaji { get; }
    public bool ShowTranslation { get; }
    public string LanguageMode { get; }
    public string InitialWorkingLevel { get; }
}

/// <summary>Starting bands and their table (SPEC §9).</summary>
public static class StartingBands
{
    public const string Zero = "zero";
    public const string EarlyBeginner = "early_beginner";
    public const string DevelopingBeginner = "developing_beginner";
    public const string Intermediate = "intermediate";
    public const string Advanced = "advanced";

    private static readonly BandRow[] Rows =
    {
        new(Zero, true, true, true, true, SupportValues.LanguageEnglishExplanations, WorkingLevels.PreN5),
        new(EarlyBeginner, true, true, true, true, SupportValues.LanguageMixed, WorkingLevels.N5),
        new(DevelopingBeginner, true, true, false, true, SupportValues.LanguageMixed, WorkingLevels.N4),
        new(Intermediate, true, true, false, false, SupportValues.LanguageMixed, WorkingLevels.N3),
        new(Advanced, true, false, false, false, SupportValues.LanguageJapaneseOnly, WorkingLevels.N2),
    };

    public static IReadOnlyList<string> All { get; } = Rows.Select(r => r.Band).ToArray();

    public static IReadOnlyList<BandRow> Table => Rows;

    public static bool IsBand(string? band) => band is not null && All.Contains(band, StringComparer.Ordinal);

    public static BandRow Row(string band) =>
        Rows.FirstOrDefault(r => r.Band == band) ?? throw new ArgumentException($"unknown band '{band}'; allowed: {string.Join(", ", All)}", nameof(band));
}

/// <summary>Hidden JLPT working levels, lowest first (SPEC §9; RULING(Handler 2026-10-27) hidden JLPT level).</summary>
public static class WorkingLevels
{
    public const string PreN5 = "pre_n5";
    public const string N5 = "n5";
    public const string N4 = "n4";
    public const string N3 = "n3";
    public const string N2 = "n2";
    public const string N1 = "n1";

    /// <summary>Ordered lowest to highest.</summary>
    public static IReadOnlyList<string> Ordered { get; } = new[] { PreN5, N5, N4, N3, N2, N1 };

    /// <summary>Levels a learner may self-report (<c>self_reported_jlpt</c>).</summary>
    public static IReadOnlyList<string> SelfReportable { get; } = new[] { N5, N4, N3, N2, N1 };

    public static bool IsLevel(string? level) => level is not null && Ordered.Contains(level, StringComparer.Ordinal);

    public static int IndexOf(string level) => Ordered.ToList().IndexOf(level);

    /// <summary>One step up (<paramref name="delta"/> = 1) or down (-1), clamped to pre_n5..n1.</summary>
    public static string Step(string level, int delta)
    {
        var i = IndexOf(level);
        if (i < 0) throw new ArgumentException($"unknown level '{level}'", nameof(level));
        var j = Math.Max(0, Math.Min(Ordered.Count - 1, i + delta));
        return Ordered[j];
    }
}

/// <summary>Reasons stored on <c>level_history</c> entries.</summary>
public static class LevelChangeReasons
{
    public const string Band = "band";
    public const string SelfReport = "self_report";
    public const string LearnerRequest = "learner_request";
    public const string Adaptation = "adaptation";
    public const string EvidenceUp = "evidence_up";
    public const string EvidenceDown = "evidence_down";
}

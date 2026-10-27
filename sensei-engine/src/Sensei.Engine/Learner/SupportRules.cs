using System;
using System.Collections.Generic;

namespace Sensei.Engine.Learner;

/// <summary>Who changed a preference (<c>preference_changes.by</c>).</summary>
public static class PreferenceActors
{
    public const string Player = "player";
    public const string Sensei = "sensei";
}

/// <summary>
/// Support, pinning, band and working-level mutations shared by the player API (S40, S42) and <c>set_support</c> (S41, S103).
/// Every method mutates the live profile: call under the engine lock, after validating the whole call.
/// </summary>
public static class SupportRules
{
    /// <summary>S40: sets every field of <paramref name="patch"/> and pins it; records actual changes as <c>player</c>.</summary>
    public static void ApplyPlayerPatch(LearnerProfile profile, SessionRecord? session, SupportPatch patch)
    {
        foreach (var field in patch.FieldNames())
        {
            SetField(profile, session, field, PatchValue(patch, field), PreferenceActors.Player);
            if (!profile.Pinned.Contains(field)) profile.Pinned.Add(field);
        }
    }

    /// <summary>
    /// S41/S42/S103: sets the band and applies its row to every unpinned display field and to <c>language_mode</c> when
    /// unpinned, then sets the working level to the band's level (reason <c>band</c>). Records changes as <paramref name="by"/>.
    /// </summary>
    public static void ApplyStartingBand(LearnerProfile profile, SessionRecord? session, string band, string by, DateTimeOffset at)
    {
        var row = StartingBands.Row(band);
        if (profile.StartingBand != band)
        {
            RecordChange(session, SupportFields.StartingBand, profile.StartingBand, band, by);
            profile.StartingBand = band;
        }
        SetIfUnpinned(profile, session, SupportFields.ShowJapanese, Bool(row.ShowJapanese), by);
        SetIfUnpinned(profile, session, SupportFields.ShowReading, Bool(row.ShowReading), by);
        SetIfUnpinned(profile, session, SupportFields.ShowRomaji, Bool(row.ShowRomaji), by);
        SetIfUnpinned(profile, session, SupportFields.ShowTranslation, Bool(row.ShowTranslation), by);
        SetIfUnpinned(profile, session, SupportFields.LanguageMode, row.LanguageMode, by);
        SetWorkingLevel(profile, row.InitialWorkingLevel, LevelChangeReasons.Band, at);
    }

    /// <summary>
    /// Sets one support field from its string form (<c>true</c>/<c>false</c> for booleans) and records an actual change.
    /// Does not pin and does not check pins. Returns true when the value changed.
    /// </summary>
    public static bool SetField(LearnerProfile profile, SessionRecord? session, string field, string value, string by)
    {
        var s = profile.Support;
        var from = s.GetFieldValue(field);
        if (from == value) return false;
        switch (field)
        {
            case SupportFields.ShowJapanese: s.ShowJapanese = ParseBool(value); break;
            case SupportFields.ShowReading: s.ShowReading = ParseBool(value); break;
            case SupportFields.ShowRomaji: s.ShowRomaji = ParseBool(value); break;
            case SupportFields.ShowTranslation: s.ShowTranslation = ParseBool(value); break;
            case SupportFields.Pace: s.Pace = value; break;
            case SupportFields.CorrectionStyle: s.CorrectionStyle = value; break;
            case SupportFields.LanguageMode: s.LanguageMode = value; break;
            default: throw new ArgumentException($"unknown support field '{field}'", nameof(field));
        }
        RecordChange(session, field, from, value, by);
        return true;
    }

    /// <summary>
    /// Sets the hidden working level; appends <c>{from, to, reason, at}</c> to <c>level_history</c> only on an actual change (S102).
    /// Never recorded in <c>preference_changes</c> (the level is never shown to the learner).
    /// </summary>
    public static bool SetWorkingLevel(LearnerProfile profile, string level, string reason, DateTimeOffset at, List<string>? evidenceIds = null)
    {
        if (!WorkingLevels.IsLevel(level)) throw new ArgumentException($"unknown working level '{level}'", nameof(level));
        if (profile.WorkingLevel == level) return false;
        profile.LevelHistory.Add(new LevelChange
        {
            From = profile.WorkingLevel,
            To = level,
            Reason = reason,
            At = at,
            EvidenceIds = evidenceIds,
        });
        profile.WorkingLevel = level;
        return true;
    }

    /// <summary>Appends a <c>preference_changes</c> entry to the session.</summary>
    public static void RecordChange(SessionRecord? session, string field, string? from, string? to, string by) =>
        session?.PreferenceChanges.Add(new PreferenceChange { Field = field, From = from, To = to, By = by });

    private static void SetIfUnpinned(LearnerProfile profile, SessionRecord? session, string field, string value, string by)
    {
        if (!profile.IsPinned(field)) SetField(profile, session, field, value, by);
    }

    private static string PatchValue(SupportPatch patch, string field) => field switch
    {
        SupportFields.ShowJapanese => Bool(patch.ShowJapanese!.Value),
        SupportFields.ShowReading => Bool(patch.ShowReading!.Value),
        SupportFields.ShowRomaji => Bool(patch.ShowRomaji!.Value),
        SupportFields.ShowTranslation => Bool(patch.ShowTranslation!.Value),
        SupportFields.Pace => patch.Pace!,
        SupportFields.CorrectionStyle => patch.CorrectionStyle!,
        SupportFields.LanguageMode => patch.LanguageMode!,
        _ => throw new ArgumentException($"unknown support field '{field}'", nameof(field)),
    };

    private static string Bool(bool b) => b ? "true" : "false";

    private static bool ParseBool(string value) => value switch
    {
        "true" => true,
        "false" => false,
        _ => throw new ArgumentException($"'{value}' is not true or false", nameof(value)),
    };
}

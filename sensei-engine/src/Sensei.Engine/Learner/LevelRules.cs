using System;
using System.Collections.Generic;
using System.Linq;

namespace Sensei.Engine.Learner;

// owned by s2
/// <summary>
/// Evidence-driven working-level rules (S51, S100, S102). Callers hold the engine lock; every level change goes through
/// <see cref="SupportRules.SetWorkingLevel"/>, so only an actual change appends to <c>level_history</c>.
/// </summary>
/// <remarks>
/// The window is "resolved, non-superseded attempts since the last level change" (S51). The last level change is the last
/// non-superseded <c>level_history</c> entry; the window is the evidence at positions <c>&gt;= attempt_count</c> of that entry
/// (S122), in profile order. Timestamps are never adjusted and never decide the window; an entry without
/// <c>attempt_count</c> reads as 0.
/// </remarks>
public static class LevelRules
{
    /// <summary>S51 up: resolved attempts in the window needed.</summary>
    public const int UpMinResolved = 8;

    /// <summary>S51 up: independent successes needed.</summary>
    public const int UpMinIndependent = 6;

    /// <summary>S51 up: fresh variations among the independent successes needed.</summary>
    public const int UpMinFresh = 2;

    /// <summary>S51 down: how many of the latest resolved attempts are examined (and needed in the window, S102).</summary>
    public const int DownWindow = 6;

    /// <summary>S51 down: confirmed-incorrect attempts needed among the examined ones.</summary>
    public const int DownMinIncorrect = 3;

    /// <summary>S51 down: different targets those attempts must span.</summary>
    public const int DownMinTargets = 2;

    /// <summary>S51/S102/S122: after a committed attempt, applies the up/down rule over resolved attempts since the last level change.</summary>
    public static void ApplyEvidenceLevelRules(LearnerProfile profile, DateTimeOffset at)
    {
        var start = WindowStart(LastEffectiveChange(profile));
        var window = new List<EvidenceRecord>();
        for (var i = start; i < profile.Evidence.Count; i++)
        {
            var e = profile.Evidence[i];
            if (!e.Superseded && EvidenceRules.IsResolved(e)) window.Add(e);
        }
        var decision = Decide(profile.WorkingLevel, window);
        if (decision is not null) SupportRules.SetWorkingLevel(profile, decision.Level, decision.Reason, at, decision.EvidenceIds);
    }

    /// <summary>
    /// S100: after attempts were superseded, marks the first evidence-driven <c>level_history</c> entry whose evidence includes a
    /// superseded attempt, and every later evidence-driven entry, as superseded (band, self-report and difficulty entries never are),
    /// then recomputes the working level: it starts from the last non-superseded entry's level (or the level before the first
    /// entry) and replays S51 over the non-superseded attempts after that entry. Replayed level changes append new entries.
    /// </summary>
    public static void RecomputeAfterSupersede(LearnerProfile profile, DateTimeOffset at)
    {
        var history = profile.LevelHistory;
        var superseded = new HashSet<string>(profile.Evidence.Where(e => e.Superseded).Select(e => e.AttemptId), StringComparer.Ordinal);
        var first = history.FindIndex(h => !h.Superseded && IsEvidenceDriven(h) && h.EvidenceIds is not null && h.EvidenceIds.Any(superseded.Contains));
        if (first >= 0)
        {
            for (var i = first; i < history.Count; i++)
                if (IsEvidenceDriven(history[i])) history[i].Superseded = true;
        }

        var anchor = LastEffectiveChange(profile);
        profile.WorkingLevel = anchor?.To ?? (history.Count > 0 ? history[0].From : profile.WorkingLevel);
        var window = new List<EvidenceRecord>();
        for (var i = WindowStart(anchor); i < profile.Evidence.Count; i++)
        {
            var e = profile.Evidence[i];
            if (e.Superseded || !EvidenceRules.IsResolved(e)) continue;
            window.Add(e);
            var decision = Decide(profile.WorkingLevel, window);
            if (decision is null) continue;
            if (SupportRules.SetWorkingLevel(profile, decision.Level, decision.Reason, at, decision.EvidenceIds))
                profile.LevelHistory[profile.LevelHistory.Count - 1].AttemptCount = i + 1; // S122: the change as it would have happened at this attempt
            window.Clear();
        }
    }

    private static LevelChange? LastEffectiveChange(LearnerProfile profile) => profile.LevelHistory.LastOrDefault(h => !h.Superseded);

    private static bool IsEvidenceDriven(LevelChange change) =>
        change.Reason == LevelChangeReasons.EvidenceUp || change.Reason == LevelChangeReasons.EvidenceDown;

    /// <summary>S122: the first evidence position of the window after <paramref name="anchor"/> (0 with no level change).</summary>
    private static int WindowStart(LevelChange? anchor) => Math.Max(0, anchor?.AttemptCount ?? 0);

    /// <summary>The rule decision for <paramref name="window"/> (resolved, non-superseded attempts since the last change) at <paramref name="level"/>.</summary>
    private static Decision? Decide(string level, IReadOnlyList<EvidenceRecord> window)
    {
        var index = WorkingLevels.IndexOf(level);
        if (index < WorkingLevels.Ordered.Count - 1 && window.Count >= UpMinResolved)
        {
            var independent = window.Where(a => a.Outcome == AttemptOutcomes.IndependentSuccess).ToList();
            if (independent.Count >= UpMinIndependent && independent.Count(a => a.IsFreshVariation) >= UpMinFresh)
                return new Decision(WorkingLevels.Step(level, 1), LevelChangeReasons.EvidenceUp, window.Select(a => a.AttemptId).ToList());
        }
        if (index > 0 && window.Count >= DownWindow)
        {
            var last = window.Skip(window.Count - DownWindow).ToList();
            var incorrect = last.Where(a => a.Outcome == AttemptOutcomes.ConfirmedIncorrect).ToList();
            if (incorrect.Count >= DownMinIncorrect && incorrect.Select(a => (a.Target.Dimension, a.Target.Key)).Distinct().Count() >= DownMinTargets)
                return new Decision(WorkingLevels.Step(level, -1), LevelChangeReasons.EvidenceDown, last.Select(a => a.AttemptId).ToList());
        }
        return null;
    }

    private sealed record Decision(string Level, string Reason, List<string> EvidenceIds);
}

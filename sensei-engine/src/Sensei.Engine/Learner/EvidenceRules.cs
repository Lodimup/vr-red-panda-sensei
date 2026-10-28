using System.Collections.Generic;
using System.Linq;
using Sensei.Engine.Tasks;

namespace Sensei.Engine.Learner;

// owned by s2
/// <summary>
/// Evidence rules (<c>rules-v1</c>): relevant aids, outcomes, labels, recommendations, review queue and summary rows.
/// A target's identity is the pair <c>(dimension, key)</c> everywhere (S97); targets are listed in order of their first attempt.
/// </summary>
public static class EvidenceRules
{
    /// <summary>The rule version stored on every evidence record (S46).</summary>
    public const string RuleVersion = "rules-v1";

    /// <summary>S50: how many of a target's most recent resolved attempts the recommendations look at.</summary>
    public const int RecommendationWindow = 4;

    private static readonly string[] ListeningAids =
    {
        AssistanceKinds.TranscriptVisible, AssistanceKinds.TranslationVisible, AssistanceKinds.HighlightAnswer,
        AssistanceKinds.Demonstration, AssistanceKinds.Repetition,
    };

    private static readonly string[] VocabularyAids =
    {
        AssistanceKinds.ModelAnswer, AssistanceKinds.PartialPhrase, AssistanceKinds.VocabularyCard,
        AssistanceKinds.AudioModel, AssistanceKinds.Demonstration,
    };

    private static readonly string[] ProductionAids =
    {
        AssistanceKinds.ModelAnswer, AssistanceKinds.PartialPhrase, AssistanceKinds.AudioModel, AssistanceKinds.Demonstration,
    };

    private static readonly string[] ReadingAids =
    {
        AssistanceKinds.TranslationVisible, AssistanceKinds.TranslationHint, AssistanceKinds.AudioModel, AssistanceKinds.HighlightAnswer,
    };

    private static readonly string[] InteractionAids = { AssistanceKinds.ModelAnswer, AssistanceKinds.Demonstration };

    /// <summary>S44: the aid kinds that count as help for a target of <paramref name="dimension"/>.</summary>
    public static IReadOnlyList<string> RelevantAids(string dimension) => dimension switch
    {
        SkillDimensions.Listening => ListeningAids,
        SkillDimensions.Vocabulary => VocabularyAids,
        SkillDimensions.Production => ProductionAids,
        SkillDimensions.Reading => ReadingAids,
        SkillDimensions.Interaction => InteractionAids,
        SkillDimensions.Register => InteractionAids,
        _ => System.Array.Empty<string>(),
    };

    /// <summary>S45: outcome of an attempt from its assessment, communicative success and the aids counted for it (S44, S94).</summary>
    public static string ClassifyOutcome(string languageAssessment, bool communicativeSuccess, IReadOnlyCollection<string> aids)
    {
        if (languageAssessment == LanguageAssessments.UnclearInput) return AttemptOutcomes.Unresolved;
        if (!communicativeSuccess) return AttemptOutcomes.ConfirmedIncorrect;
        if (aids is null || aids.Count == 0) return AttemptOutcomes.IndependentSuccess;
        if (aids.Contains(AssistanceKinds.Demonstration)) return AttemptOutcomes.CompletedByDemonstration;
        if (aids.Contains(AssistanceKinds.ModelAnswer) || aids.Contains(AssistanceKinds.AudioModel)) return AttemptOutcomes.SuccessAfterModel;
        if (aids.Any(a => a != AssistanceKinds.Repetition)) return AttemptOutcomes.SuccessAfterHint;
        return AttemptOutcomes.SuccessAfterRepetition;
    }

    /// <summary>
    /// S44, S94: the aid kinds counted for an attempt at <c>(dimension, key)</c>, in the order they were recorded, then the visibility aids.
    /// Records count when relevant to the dimension and made strictly after <paramref name="boundarySequence"/> and strictly before
    /// <paramref name="sourceSequence"/>; <c>audio_model</c> must also have been presented after <paramref name="previousUtteranceSequence"/>.
    /// For <c>listening</c> targets visible Japanese counts as <c>transcript_visible</c> and visible translation as
    /// <c>translation_visible</c>; for <c>reading</c> targets visible translation counts as <c>translation_visible</c>
    /// (<paramref name="supportAtSource"/> is the snapshot when the source arrived).
    /// </summary>
    public static IReadOnlyList<string> CountedAids(string dimension, string key, long boundarySequence, long sourceSequence,
        long previousUtteranceSequence, SupportSettings supportAtSource, IEnumerable<AssistanceRecord> records)
    {
        var relevant = RelevantAids(dimension);
        var kinds = new List<string>();
        foreach (var record in records.Where(r => r.AppliesTo(dimension, key) && r.Sequence > boundarySequence && r.Sequence < sourceSequence).OrderBy(r => r.Sequence))
        {
            if (!relevant.Contains(record.Kind)) continue;
            if (record.Kind == AssistanceKinds.AudioModel && record.Sequence <= previousUtteranceSequence) continue;
            if (!kinds.Contains(record.Kind)) kinds.Add(record.Kind);
        }

        void AddVisible(string kind)
        {
            if (!kinds.Contains(kind)) kinds.Add(kind);
        }

        if (dimension == SkillDimensions.Listening)
        {
            if (supportAtSource.ShowJapanese) AddVisible(AssistanceKinds.TranscriptVisible);
            if (supportAtSource.ShowTranslation) AddVisible(AssistanceKinds.TranslationVisible);
        }
        else if (dimension == SkillDimensions.Reading)
        {
            if (supportAtSource.ShowTranslation) AddVisible(AssistanceKinds.TranslationVisible);
        }
        return kinds;
    }

    /// <summary>True for an outcome in which the learner got the meaning across (everything except <c>unresolved</c> and <c>confirmed_incorrect</c>).</summary>
    public static bool IsSuccess(string outcome) =>
        outcome == AttemptOutcomes.IndependentSuccess || outcome == AttemptOutcomes.SuccessAfterModel ||
        outcome == AttemptOutcomes.SuccessAfterHint || outcome == AttemptOutcomes.SuccessAfterRepetition ||
        outcome == AttemptOutcomes.CompletedByDemonstration;

    /// <summary>A resolved attempt is one whose outcome is not <c>unresolved</c> (S49, S50, S51).</summary>
    public static bool IsResolved(EvidenceRecord e) => e.Outcome != AttemptOutcomes.Unresolved;

    /// <summary>
    /// S49, S97, S101: one label row per target <c>(dimension, key)</c> over non-superseded attempts, in order of first attempt.
    /// <paramref name="evidence"/> is the whole profile's evidence. <c>last_outcome</c> is the outcome of the latest non-superseded
    /// attempt; <c>attempts</c> counts the non-superseded attempts.
    /// </summary>
    public static IReadOnlyList<TargetLabel> ComputeLabels(IReadOnlyList<EvidenceRecord> evidence)
    {
        var labels = new List<TargetLabel>();
        foreach (var (target, attempts) in GroupByTarget(evidence))
        {
            labels.Add(new TargetLabel
            {
                Key = target.Key,
                Dimension = target.Dimension,
                Label = LabelOf(attempts),
                LastOutcome = attempts[attempts.Count - 1].Outcome,
                Attempts = attempts.Count,
            });
        }
        return labels;
    }

    /// <summary>
    /// S50, S97: recommendations per target over its last <see cref="RecommendationWindow"/> resolved, non-superseded attempts:
    /// <c>step_up</c> when at least 3 are independent successes and one of those is a fresh variation; <c>offer_simpler</c>
    /// when at least 2 are confirmed incorrect.
    /// </summary>
    public static IReadOnlyList<Recommendation> ComputeRecommendations(IReadOnlyList<EvidenceRecord> evidence)
    {
        var recommendations = new List<Recommendation>();
        foreach (var (target, attempts) in GroupByTarget(evidence))
        {
            var last = attempts.Where(IsResolved).ToList();
            if (last.Count > RecommendationWindow) last = last.GetRange(last.Count - RecommendationWindow, RecommendationWindow);
            var independent = last.Where(a => a.Outcome == AttemptOutcomes.IndependentSuccess).ToList();
            if (independent.Count >= 3 && independent.Any(a => a.IsFreshVariation))
                recommendations.Add(new Recommendation { Key = target.Key, Dimension = target.Dimension, Kind = RecommendationKinds.StepUp });
            else if (last.Count(a => a.Outcome == AttemptOutcomes.ConfirmedIncorrect) >= 2)
                recommendations.Add(new Recommendation { Key = target.Key, Dimension = target.Dimension, Kind = RecommendationKinds.OfferSimpler });
        }
        return recommendations;
    }

    /// <summary>
    /// S52, S97, S101: targets <c>(dimension, key)</c> due for review, in order of first attempt; two dimensions sharing a key are two
    /// queue items. A target is due when its latest resolved outcome is <c>confirmed_incorrect</c>, its label is <c>used_with_help</c>,
    /// its latest attempt is <c>unresolved</c>, or its label is <c>used_independently</c> with every independent success in
    /// <paramref name="currentSessionId"/>.
    /// </summary>
    public static IReadOnlyList<(string Dimension, string Key)> ComputeReviewDue(IReadOnlyList<EvidenceRecord> evidence, string currentSessionId)
    {
        var due = new List<(string Dimension, string Key)>();
        foreach (var (target, attempts) in GroupByTarget(evidence))
        {
            var label = LabelOf(attempts);
            var latestResolved = attempts.LastOrDefault(IsResolved);
            var independent = attempts.Where(a => a.Outcome == AttemptOutcomes.IndependentSuccess).ToList();
            var isDue = latestResolved?.Outcome == AttemptOutcomes.ConfirmedIncorrect
                        || label == TargetLabels.UsedWithHelp
                        || attempts[attempts.Count - 1].Outcome == AttemptOutcomes.Unresolved
                        || (label == TargetLabels.UsedIndependently && independent.All(a => a.SessionId == currentSessionId));
            if (isDue) due.Add(target);
        }
        return due;
    }

    /// <summary>
    /// S55: <c>practiced</c> rows of the session summary for <paramref name="sessionId"/> (targets with a non-superseded attempt in it,
    /// in order of their first attempt in it), labels computed over all evidence. <c>independent</c> counts independent successes,
    /// <c>supported</c> the other successes, <c>unresolved</c> the unresolved attempts of this session.
    /// </summary>
    public static IReadOnlyList<PracticedTarget> BuildPracticed(IReadOnlyList<EvidenceRecord> evidence, string sessionId)
    {
        var labels = ComputeLabels(evidence).ToDictionary(l => (l.Dimension, l.Key));
        var rows = new List<PracticedTarget>();
        foreach (var (target, attempts) in GroupByTarget(evidence.Where(e => e.SessionId == sessionId).ToList()))
        {
            rows.Add(new PracticedTarget
            {
                Key = target.Key,
                Dimension = target.Dimension,
                Label = labels.TryGetValue((target.Dimension, target.Key), out var label) ? label.Label : TargetLabels.Practiced,
                Independent = attempts.Count(a => a.Outcome == AttemptOutcomes.IndependentSuccess),
                Supported = attempts.Count(a => IsSuccess(a.Outcome) && a.Outcome != AttemptOutcomes.IndependentSuccess),
                Unresolved = attempts.Count(a => a.Outcome == AttemptOutcomes.Unresolved),
            });
        }
        return rows;
    }

    /// <summary>S55: <c>unresolved_keys</c> of the session summary: keys whose latest attempt in <paramref name="sessionId"/> is unresolved.</summary>
    public static IReadOnlyList<string> BuildUnresolvedKeys(IReadOnlyList<EvidenceRecord> evidence, string sessionId)
    {
        var keys = new List<string>();
        foreach (var (target, attempts) in GroupByTarget(evidence.Where(e => e.SessionId == sessionId).ToList()))
            if (attempts[attempts.Count - 1].Outcome == AttemptOutcomes.Unresolved && !keys.Contains(target.Key)) keys.Add(target.Key);
        return keys;
    }

    /// <summary>S49/S101: label of one target from its non-superseded attempts.</summary>
    internal static string LabelOf(IReadOnlyList<EvidenceRecord> attempts)
    {
        var resolved = attempts.Where(IsResolved).ToList();
        var independent = resolved.Where(a => a.Outcome == AttemptOutcomes.IndependentSuccess).ToList();
        if (independent.Select(a => a.SessionId).Distinct().Count() >= 2) return TargetLabels.Stable;
        if (independent.Count >= 1) return TargetLabels.UsedIndependently;
        if (resolved.Any(a => IsSuccess(a.Outcome))) return TargetLabels.UsedWithHelp;
        return TargetLabels.Practiced;
    }

    /// <summary>Non-superseded attempts grouped by target <c>(dimension, key)</c>, groups in order of first attempt, attempts in evidence order.</summary>
    private static List<((string Dimension, string Key) Target, List<EvidenceRecord> Attempts)> GroupByTarget(IReadOnlyList<EvidenceRecord> evidence)
    {
        var order = new List<(string Dimension, string Key)>();
        var groups = new Dictionary<(string Dimension, string Key), List<EvidenceRecord>>();
        foreach (var e in evidence)
        {
            if (e.Superseded) continue;
            var id = (e.Target.Dimension, e.Target.Key);
            if (!groups.TryGetValue(id, out var list))
            {
                list = new List<EvidenceRecord>();
                groups[id] = list;
                order.Add(id);
            }
            list.Add(e);
        }
        return order.Select(id => (id, groups[id])).ToList();
    }
}

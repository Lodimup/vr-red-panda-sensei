using System.Collections.Generic;

namespace Sensei.Engine.Learner;

// owned by s2
/// <summary>
/// Evidence rules (<c>rules-v1</c>): outcomes, labels, recommendations, review queue and summary rows.
/// Signatures are final; the learner slice implements the bodies.
/// </summary>
public static class EvidenceRules
{
    /// <summary>The rule version stored on every evidence record (S46).</summary>
    public const string RuleVersion = "rules-v1";

    /// <summary>S45: outcome of an attempt from its assessment, communicative success and the aids counted for it (S44, S94).</summary>
    public static string ClassifyOutcome(string languageAssessment, bool communicativeSuccess, IReadOnlyCollection<string> aids)
    {
        // s2 implements body
        return AttemptOutcomes.Unresolved;
    }

    /// <summary>
    /// S49, S97, S101: one label row per target <c>(dimension, key)</c> over non-superseded attempts, in order of first attempt.
    /// <paramref name="evidence"/> is the whole profile's evidence.
    /// </summary>
    public static IReadOnlyList<TargetLabel> ComputeLabels(IReadOnlyList<EvidenceRecord> evidence)
    {
        // s2 implements body
        return new List<TargetLabel>();
    }

    /// <summary>S50, S97: recommendations per target over its last 4 resolved, non-superseded attempts.</summary>
    public static IReadOnlyList<Recommendation> ComputeRecommendations(IReadOnlyList<EvidenceRecord> evidence)
    {
        // s2 implements body
        return new List<Recommendation>();
    }

    /// <summary>S52, S101: target keys due for review; <paramref name="currentSessionId"/> scopes the "only within this session" rule.</summary>
    public static IReadOnlyList<string> ComputeReviewDue(IReadOnlyList<EvidenceRecord> evidence, string currentSessionId)
    {
        // s2 implements body
        return new List<string>();
    }

    /// <summary>S55: <c>practiced</c> rows of the session summary for <paramref name="sessionId"/>, labels computed over all evidence.</summary>
    public static IReadOnlyList<PracticedTarget> BuildPracticed(IReadOnlyList<EvidenceRecord> evidence, string sessionId)
    {
        // s2 implements body
        return new List<PracticedTarget>();
    }

    /// <summary>S55: <c>unresolved_keys</c> of the session summary for <paramref name="sessionId"/>.</summary>
    public static IReadOnlyList<string> BuildUnresolvedKeys(IReadOnlyList<EvidenceRecord> evidence, string sessionId)
    {
        // s2 implements body
        return new List<string>();
    }
}

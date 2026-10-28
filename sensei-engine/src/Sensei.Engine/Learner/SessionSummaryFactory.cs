using System.Linq;

namespace Sensei.Engine.Learner;

/// <summary>Builds the learner-facing <see cref="SessionSummary"/> (S55). Never includes the working level.</summary>
public static class SessionSummaryFactory
{
    /// <summary>The summary of <paramref name="sessionId"/> from committed, non-superseded evidence. Call under the engine lock.</summary>
    public static SessionSummary Build(LearnerProfile profile, string sessionId)
    {
        var session = profile.FindSession(sessionId);
        var evidence = profile.Evidence;
        return new SessionSummary
        {
            SessionId = sessionId,
            Practiced = EvidenceRules.BuildPracticed(evidence, sessionId).ToList(),
            UnresolvedKeys = EvidenceRules.BuildUnresolvedKeys(evidence, sessionId).ToList(),
            NextSuggestion = session?.NextSuggestion,
            PreferenceChanges = session?.PreferenceChanges.Select(c => new PreferenceChange { Field = c.Field, From = c.From, To = c.To, By = c.By }).ToList()
                ?? new System.Collections.Generic.List<PreferenceChange>(),
        };
    }
}

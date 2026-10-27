using System;

namespace Sensei.Engine.Learner;

// owned by s2
/// <summary>
/// Evidence-driven working-level rules (S51, S100, S102). Signatures are final; the learner slice implements the bodies.
/// Callers hold the engine lock; implementations change the level through <see cref="SupportRules.SetWorkingLevel"/>.
/// </summary>
public static class LevelRules
{
    /// <summary>S51/S102: after a committed attempt, applies the up/down rule over resolved attempts since the last level change.</summary>
    public static void ApplyEvidenceLevelRules(LearnerProfile profile, DateTimeOffset at)
    {
        // s2 implements body
    }

    /// <summary>
    /// S100: after attempts were superseded, marks affected evidence-driven <c>level_history</c> entries (and all later
    /// evidence-driven ones) superseded and recomputes the working level by replaying S51.
    /// </summary>
    public static void RecomputeAfterSupersede(LearnerProfile profile, DateTimeOffset at)
    {
        // s2 implements body
    }
}

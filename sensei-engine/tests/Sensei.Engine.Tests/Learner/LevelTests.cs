using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Learner.LearnerKit;

namespace Sensei.Engine.Tests.Learner;

/// <summary>Evidence-driven working level: S51, S100, S102 (and the hidden-level rulings of §9).</summary>
public sealed class LevelTests
{
    private const string I = AttemptOutcomes.IndependentSuccess;
    private const string X = AttemptOutcomes.ConfirmedIncorrect;
    private const string U = AttemptOutcomes.Unresolved;
    private const string H = AttemptOutcomes.SuccessAfterHint;

    /// <summary>Adds six independent successes, <paramref name="fresh"/> of them fresh variations.</summary>
    private static void SixIndependent(LearnerProfile p, int fresh)
    {
        for (var i = 0; i < 6; i++) Add(p, I, fresh: i < fresh);
    }

    // ---------------------------------------------------------------- S51 up

    [Fact]
    public void S51_up_needs_8_resolved_attempts_7_is_not_enough_and_8_is()
    {
        var p = NewProfile(WorkingLevels.N3);
        SixIndependent(p, 2);
        Add(p, X);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Assert.Empty(p.LevelHistory);
        Add(p, X);
        Assert.Equal(WorkingLevels.N2, p.WorkingLevel);
        Assert.Single(p.LevelHistory);
    }

    [Fact]
    public void S51_up_needs_6_independent_successes_5_is_not_enough_and_6_is()
    {
        var p = NewProfile(WorkingLevels.N3);
        for (var i = 0; i < 5; i++) Add(p, I, fresh: i < 2);
        Add(p, H);
        Add(p, H);
        Add(p, X);
        Assert.Equal(8, p.Evidence.Count);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);

        var q = NewProfile(WorkingLevels.N3);
        for (var i = 0; i < 6; i++) Add(q, I, fresh: i < 2);
        Add(q, H);
        Add(q, X);
        Assert.Equal(WorkingLevels.N2, q.WorkingLevel);
    }

    [Fact]
    public void S51_up_needs_2_fresh_variations_one_is_not_enough_and_two_is()
    {
        var p = NewProfile(WorkingLevels.N3);
        SixIndependent(p, 1);
        Add(p, X);
        Add(p, X);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);

        var q = NewProfile(WorkingLevels.N3);
        SixIndependent(q, 2);
        Add(q, X);
        Add(q, X);
        Assert.Equal(WorkingLevels.N2, q.WorkingLevel);
    }

    [Fact]
    public void S51_a_later_fresh_independent_success_completes_the_missing_variation_and_the_window_keeps_growing()
    {
        var p = NewProfile(WorkingLevels.N3);
        SixIndependent(p, 1);
        Add(p, X);
        Add(p, X);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Add(p, I, fresh: true);
        Assert.Equal(WorkingLevels.N2, p.WorkingLevel);
        Assert.Equal(9, p.LevelHistory[0].EvidenceIds!.Count);
    }

    [Fact]
    public void S51_fresh_variations_are_counted_among_the_independent_successes_only()
    {
        var p = NewProfile(WorkingLevels.N3);
        SixIndependent(p, 0);
        Add(p, X, fresh: true);
        Add(p, X, fresh: true);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
    }

    [Fact]
    public void S51_helped_successes_are_resolved_but_not_independent()
    {
        var p = NewProfile(WorkingLevels.N3);
        for (var i = 0; i < 5; i++) Add(p, I, fresh: i < 2);
        for (var i = 0; i < 5; i++) Add(p, H, fresh: true);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
    }

    [Fact]
    public void S51_unresolved_attempts_do_not_count_toward_the_window()
    {
        var p = NewProfile(WorkingLevels.N3);
        SixIndependent(p, 2);
        Add(p, X);
        for (var i = 0; i < 5; i++) Add(p, U);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Add(p, X);
        Assert.Equal(WorkingLevels.N2, p.WorkingLevel);
    }

    [Fact]
    public void S51_superseded_attempts_do_not_count_toward_the_window()
    {
        var p = NewProfile(WorkingLevels.N3);
        SixIndependent(p, 2);
        Add(p, X);
        var gone = Add(p, X);
        Assert.Equal(WorkingLevels.N2, p.WorkingLevel);

        var q = NewProfile(WorkingLevels.N3);
        SixIndependent(q, 2);
        Add(q, X);
        q.Evidence.Add(Ev(X, superseded: true));
        LevelRules.ApplyEvidenceLevelRules(q, DateTimeOffset.UtcNow);
        Assert.Equal(WorkingLevels.N3, q.WorkingLevel);
        _ = gone;
    }

    [Fact]
    public void S51_the_up_entry_records_from_to_reason_time_and_every_window_attempt()
    {
        var p = NewProfile(WorkingLevels.N4);
        SixIndependent(p, 2);
        Add(p, X);
        var last = Add(p, X);
        var entry = p.LevelHistory.Single();
        Assert.Equal((WorkingLevels.N4, WorkingLevels.N3, "evidence_up"), (entry.From, entry.To, entry.Reason));
        Assert.Equal(last.At, entry.At);
        Assert.Equal(p.Evidence.Select(e => e.AttemptId), entry.EvidenceIds);
        Assert.False(entry.Superseded);
    }

    [Fact]
    public void S102_at_n1_the_upward_rule_is_not_evaluated()
    {
        var p = NewProfile(WorkingLevels.N1);
        SixIndependent(p, 2);
        Add(p, X);
        Add(p, X);
        Assert.Equal(WorkingLevels.N1, p.WorkingLevel);
        Assert.Empty(p.LevelHistory);
    }

    [Fact]
    public void S51_the_window_restarts_after_a_level_change_and_the_next_change_needs_8_new_attempts()
    {
        var p = NewProfile(WorkingLevels.N5);
        SixIndependent(p, 2);
        Add(p, X);
        Add(p, X);
        Assert.Equal(WorkingLevels.N4, p.WorkingLevel);
        for (var i = 0; i < 6; i++) Add(p, I, fresh: true);
        Add(p, X);
        Assert.Equal(WorkingLevels.N4, p.WorkingLevel);
        Add(p, X);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Assert.Equal(2, p.LevelHistory.Count);
        Assert.Equal(8, p.LevelHistory[1].EvidenceIds!.Count);
        Assert.Empty(p.LevelHistory[1].EvidenceIds!.Intersect(p.LevelHistory[0].EvidenceIds!));
    }

    [Fact]
    public void S51_attempts_before_a_band_change_do_not_count_after_it()
    {
        var p = NewProfile(WorkingLevels.N5);
        for (var i = 0; i < 4; i++) Add(p, I, fresh: i < 2);
        SupportRules.SetWorkingLevel(p, WorkingLevels.N3, LevelChangeReasons.Band, p.Evidence[^1].At.AddSeconds(30));
        for (var i = 0; i < 4; i++) Add(p, I, fresh: i < 2);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Assert.Single(p.LevelHistory);
        for (var i = 0; i < 2; i++) Add(p, I);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Add(p, I);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Add(p, I);
        Assert.Equal(WorkingLevels.N2, p.WorkingLevel);
    }

    [Fact]
    public void S51_without_the_band_change_the_same_eight_attempts_would_have_moved_the_level()
    {
        var p = NewProfile(WorkingLevels.N5);
        for (var i = 0; i < 8; i++) Add(p, I, fresh: i < 2);
        Assert.Equal(WorkingLevels.N4, p.WorkingLevel);
    }

    // ---------------------------------------------------------------- S51 down

    private static LearnerProfile Down(string level, params (string Outcome, string Key)[] attempts)
    {
        var p = NewProfile(level);
        foreach (var (outcome, key) in attempts) Add(p, outcome, key);
        return p;
    }

    [Fact]
    public void S51_down_needs_3_confirmed_incorrect_in_the_last_6_two_is_not_enough_and_three_is()
    {
        var two = Down(WorkingLevels.N3, (X, "a"), (X, "b"), (I, "a"), (I, "a"), (I, "a"), (I, "a"));
        Assert.Equal(WorkingLevels.N3, two.WorkingLevel);
        var three = Down(WorkingLevels.N3, (X, "a"), (X, "b"), (X, "a"), (I, "a"), (I, "a"), (I, "a"));
        Assert.Equal(WorkingLevels.N4, three.WorkingLevel);
    }

    [Fact]
    public void S51_down_needs_at_least_2_different_target_keys_among_the_incorrect_attempts()
    {
        var oneKey = Down(WorkingLevels.N3, (X, "a"), (X, "a"), (X, "a"), (I, "a"), (I, "a"), (I, "a"));
        Assert.Equal(WorkingLevels.N3, oneKey.WorkingLevel);
        var twoKeys = Down(WorkingLevels.N3, (X, "a"), (X, "a"), (X, "b"), (I, "a"), (I, "a"), (I, "a"));
        Assert.Equal(WorkingLevels.N4, twoKeys.WorkingLevel);
    }

    [Fact]
    public void S51_a_target_is_the_dimension_key_pair_so_the_same_key_in_two_dimensions_counts_as_two()
    {
        var p = NewProfile(WorkingLevels.N3);
        Add(p, X, "a", dimension: "production");
        Add(p, X, "a", dimension: "production");
        Add(p, X, "a", dimension: "vocabulary");
        for (var i = 0; i < 3; i++) Add(p, I, "a");
        Assert.Equal(WorkingLevels.N4, p.WorkingLevel);
    }

    [Fact]
    public void S102_the_last_6_requires_at_least_6_resolved_attempts_in_the_window()
    {
        var five = Down(WorkingLevels.N3, (X, "a"), (X, "b"), (X, "a"), (I, "a"), (I, "a"));
        Assert.Equal(WorkingLevels.N3, five.WorkingLevel);
        Assert.Empty(five.LevelHistory);
    }

    [Fact]
    public void S51_only_the_last_6_resolved_attempts_are_examined()
    {
        // three incorrect attempts exist in the window, but only two are within the last 6
        var p = Down(WorkingLevels.N3, (X, "a"), (X, "b"), (I, "a"), (I, "a"), (I, "a"), (I, "a"), (X, "a"));
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Assert.Equal(3, p.Evidence.Count(e => e.Outcome == X));
    }

    [Fact]
    public void S51_unresolved_attempts_do_not_count_as_incorrect_or_toward_the_last_6()
    {
        var p = Down(WorkingLevels.N3, (X, "a"), (X, "b"), (U, "a"), (U, "a"), (U, "a"), (I, "a"), (I, "a"), (I, "a"));
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Assert.Equal(8, p.Evidence.Count);
    }

    [Fact]
    public void S102_at_pre_n5_the_downward_rule_is_not_evaluated()
    {
        var p = Down(WorkingLevels.PreN5, (X, "a"), (X, "b"), (X, "a"), (I, "a"), (I, "a"), (I, "a"));
        Assert.Equal(WorkingLevels.PreN5, p.WorkingLevel);
        Assert.Empty(p.LevelHistory);
    }

    [Fact]
    public void S102_at_n1_the_downward_rule_is_still_evaluated()
    {
        var p = Down(WorkingLevels.N1, (X, "a"), (X, "b"), (X, "a"), (I, "a"), (I, "a"), (I, "a"));
        Assert.Equal(WorkingLevels.N2, p.WorkingLevel);
    }

    [Fact]
    public void S51_the_down_entry_records_the_last_6_attempts_and_the_reason()
    {
        // fires on the 7th attempt: the first attempt is in the window but outside the last 6
        var p = Down(WorkingLevels.N3, (I, "a"), (I, "a"), (X, "a"), (X, "b"), (I, "a"), (I, "a"), (X, "a"));
        Assert.Equal(WorkingLevels.N4, p.WorkingLevel);
        var entry = p.LevelHistory.Single();
        Assert.Equal((WorkingLevels.N3, WorkingLevels.N4, "evidence_down"), (entry.From, entry.To, entry.Reason));
        Assert.Equal(new[] { "a2", "a3", "a4", "a5", "a6", "a7" }, entry.EvidenceIds);
    }

    [Fact]
    public void S51_down_is_one_step_and_the_window_restarts_so_the_next_drop_needs_6_new_attempts()
    {
        var p = Down(WorkingLevels.N3, (X, "a"), (X, "b"), (X, "a"));
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Add(p, I);
        Add(p, I);
        Add(p, I);
        Assert.Equal(WorkingLevels.N4, p.WorkingLevel);
        Add(p, X, "a");
        Add(p, X, "b");
        Add(p, X, "a");
        Add(p, I);
        Add(p, I);
        Assert.Equal(WorkingLevels.N4, p.WorkingLevel);
        Add(p, I);
        Assert.Equal(WorkingLevels.N5, p.WorkingLevel);
        Assert.Equal(2, p.LevelHistory.Count);
    }

    [Fact]
    public void S51_up_is_checked_before_down()
    {
        var p = NewProfile(WorkingLevels.N3);
        for (var i = 0; i < 6; i++) Add(p, I, fresh: i < 2);
        Add(p, H);
        Add(p, H);
        Assert.Equal(WorkingLevels.N2, p.WorkingLevel);
        Assert.Equal("evidence_up", p.LevelHistory.Single().Reason);
    }

    // ---------------------------------------------------------------- through the tool

    [Fact]
    public async Task S51_through_record_attempt_the_level_rises_after_8_attempts_even_with_a_frozen_clock_and_an_earlier_band_change()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("early_beginner");
        Assert.Equal("n5", t.Engine.GetProfile().WorkingLevel);
        for (var i = 0; i < 6; i++) await SayAndAttempt(t, "production", "k" + i, "りんごをください", fresh: i < 2);
        await SayAndAttempt(t, "production", "k6", "りんごをください", success: false);
        Assert.Equal("n5", t.Engine.GetProfile().WorkingLevel);
        await SayAndAttempt(t, "production", "k7", "りんごをください", success: false);
        var profile = t.Engine.GetProfile();
        Assert.Equal("n4", profile.WorkingLevel);
        Assert.Equal(new[] { "band", "evidence_up" }, profile.LevelHistory.Select(h => h.Reason));
        Assert.Equal(8, profile.LevelHistory[1].EvidenceIds!.Count);
        Assert.Equal("n4", t.Store.Peek("default")!.WorkingLevel);
    }

    [Fact]
    public async Task S51_through_record_attempt_the_level_falls_after_3_incorrect_across_2_keys_in_6_attempts()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("intermediate");
        await SayAndAttempt(t, "production", "a", "りんごをください", success: false);
        await SayAndAttempt(t, "production", "b", "りんごをください", success: false);
        await SayAndAttempt(t, "production", "a", "りんごをください", success: false);
        await SayAndAttempt(t, "production", "c", "りんごをください");
        await SayAndAttempt(t, "production", "c", "りんごをください");
        Assert.Equal("n3", t.Engine.GetProfile().WorkingLevel);
        await SayAndAttempt(t, "production", "c", "りんごをください");
        Assert.Equal("n4", t.Engine.GetProfile().WorkingLevel);
        Assert.Equal(new[] { "band", "evidence_down" }, t.Engine.GetProfile().LevelHistory.Select(h => h.Reason));
    }

    [Fact]
    public async Task S47_duplicate_calls_add_one_attempt_to_the_level_window()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        for (var i = 0; i < 3; i++) await Attempt(t, "production", "a", "りんご", success: false);
        Assert.Single(Profile(t).Evidence);
    }

    // ---------------------------------------------------------------- S100 supersede and replay

    private static LearnerProfile UpToN5Profile()
    {
        var p = NewProfile();
        for (var i = 0; i < 6; i++) Add(p, I, fresh: i < 2);
        Add(p, X);
        Add(p, X);
        Assert.Equal(WorkingLevels.N5, p.WorkingLevel);
        return p;
    }

    [Fact]
    public void S100_superseding_an_attempt_in_an_entrys_evidence_marks_the_entry_and_restores_the_level()
    {
        var p = UpToN5Profile();
        p.Evidence[2].Superseded = true;
        LevelRules.RecomputeAfterSupersede(p, DateTimeOffset.UtcNow);
        Assert.True(p.LevelHistory.Single().Superseded);
        Assert.Equal(WorkingLevels.PreN5, p.WorkingLevel);
    }

    [Fact]
    public void S100_superseding_an_attempt_that_is_in_no_entry_changes_nothing()
    {
        var p = UpToN5Profile();
        var extra = Add(p, I);
        extra.Superseded = true;
        LevelRules.RecomputeAfterSupersede(p, DateTimeOffset.UtcNow);
        Assert.False(p.LevelHistory.Single().Superseded);
        Assert.Equal(WorkingLevels.N5, p.WorkingLevel);
    }

    [Fact]
    public void S100_every_later_evidence_driven_entry_is_marked_too_and_replay_rebuilds_the_level()
    {
        var p = UpToN5Profile();
        for (var i = 0; i < 6; i++) Add(p, I, fresh: i < 2);
        Add(p, X);
        Add(p, X);
        Assert.Equal(WorkingLevels.N4, p.WorkingLevel);
        Assert.Equal(2, p.LevelHistory.Count);

        p.Evidence[0].Superseded = true;
        LevelRules.RecomputeAfterSupersede(p, DateTimeOffset.UtcNow);
        Assert.All(p.LevelHistory.Take(2), h => Assert.True(h.Superseded));
        // Replay from pre_n5 over a2..a16: the window reaches 8 resolved attempts with 6 independent (2 fresh) at a9,
        // so the up rule fires once, then 7 more attempts are too few for a second step.
        Assert.Equal(WorkingLevels.N5, p.WorkingLevel);
        Assert.Equal(3, p.LevelHistory.Count);
        var replayed = p.LevelHistory[2];
        Assert.False(replayed.Superseded);
        Assert.Equal((WorkingLevels.PreN5, WorkingLevels.N5, "evidence_up"), (replayed.From, replayed.To, replayed.Reason));
        Assert.Equal(new[] { "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9" }, replayed.EvidenceIds);
    }

    [Fact]
    public void S100_replay_lets_the_up_rule_fire_again_when_the_remaining_attempts_still_satisfy_it()
    {
        var p = NewProfile();
        for (var i = 0; i < 7; i++) Add(p, I, fresh: i < 3);
        Add(p, X);
        Assert.Equal(WorkingLevels.N5, p.WorkingLevel);
        // supersede the incorrect attempt: 7 independent attempts remain, one short of 8 resolved
        p.Evidence[7].Superseded = true;
        LevelRules.RecomputeAfterSupersede(p, DateTimeOffset.UtcNow);
        Assert.True(p.LevelHistory[0].Superseded);
        Assert.Equal(WorkingLevels.PreN5, p.WorkingLevel);

        // supersede one independent attempt instead: 6 independent (3 fresh) + the incorrect one = 7, still short
        var q = NewProfile();
        for (var i = 0; i < 8; i++) Add(q, I, fresh: i < 3);
        Assert.Equal(WorkingLevels.N5, q.WorkingLevel);
        q.Evidence[7].Superseded = true;
        LevelRules.RecomputeAfterSupersede(q, DateTimeOffset.UtcNow);
        Assert.Equal(WorkingLevels.PreN5, q.WorkingLevel);
        var more = Add(q, I);
        Assert.Equal(WorkingLevels.N5, q.WorkingLevel);
        Assert.Equal(2, q.LevelHistory.Count);
        Assert.True(q.LevelHistory[0].Superseded);
        Assert.False(q.LevelHistory[1].Superseded);
        Assert.Contains(more.AttemptId, q.LevelHistory[1].EvidenceIds!);
        Assert.DoesNotContain("a8", q.LevelHistory[1].EvidenceIds!);
    }

    [Fact]
    public void S100_replay_can_lower_the_level_when_removing_an_attempt_exposes_three_recent_failures()
    {
        var p = NewProfile(WorkingLevels.N5);
        Add(p, X, "a");
        Add(p, X, "b");
        Add(p, I, "a");
        Add(p, I, "a");
        Add(p, I, "a");
        Add(p, I, "a");
        Add(p, X, "a");
        Assert.Equal(WorkingLevels.N5, p.WorkingLevel);
        p.Evidence[2].Superseded = true;
        LevelRules.RecomputeAfterSupersede(p, DateTimeOffset.UtcNow);
        Assert.Equal(WorkingLevels.PreN5, p.WorkingLevel);
        var entry = p.LevelHistory.Single();
        Assert.Equal((WorkingLevels.N5, WorkingLevels.PreN5, "evidence_down"), (entry.From, entry.To, entry.Reason));
        Assert.DoesNotContain("a3", entry.EvidenceIds!);
        Assert.False(entry.Superseded);
    }

    [Fact]
    public void S100_band_self_report_and_difficulty_entries_are_never_superseded()
    {
        var p = NewProfile();
        var t0 = p.CreatedAt;
        SupportRules.SetWorkingLevel(p, WorkingLevels.N3, LevelChangeReasons.Band, t0);
        SupportRules.SetWorkingLevel(p, WorkingLevels.N2, LevelChangeReasons.SelfReport, t0);
        SupportRules.SetWorkingLevel(p, WorkingLevels.N1, LevelChangeReasons.LearnerRequest, t0);
        SupportRules.SetWorkingLevel(p, WorkingLevels.N2, LevelChangeReasons.Adaptation, t0);
        var a = Ev(I);
        a.AttemptId = "a1";
        a.Superseded = true;
        p.Evidence.Add(a);
        LevelRules.RecomputeAfterSupersede(p, DateTimeOffset.UtcNow);
        Assert.All(p.LevelHistory, h => Assert.False(h.Superseded));
        Assert.Equal(WorkingLevels.N2, p.WorkingLevel);
    }

    [Fact]
    public void S100_replay_starts_from_the_last_non_superseded_entry()
    {
        var p = NewProfile();
        SupportRules.SetWorkingLevel(p, WorkingLevels.N4, LevelChangeReasons.Band, p.CreatedAt);
        for (var i = 0; i < 6; i++) Add(p, I, fresh: i < 2);
        Add(p, X);
        Add(p, X);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        p.Evidence[0].Superseded = true;
        LevelRules.RecomputeAfterSupersede(p, DateTimeOffset.UtcNow);
        Assert.True(p.LevelHistory[1].Superseded);
        Assert.Equal(WorkingLevels.N4, p.WorkingLevel);
        Assert.False(p.LevelHistory[0].Superseded);
    }

    [Fact]
    public void S100_with_no_level_history_the_current_level_is_kept_unless_the_replay_changes_it()
    {
        var p = NewProfile(WorkingLevels.N3);
        Add(p, I);
        p.Evidence[0].Superseded = true;
        LevelRules.RecomputeAfterSupersede(p, DateTimeOffset.UtcNow);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Assert.Empty(p.LevelHistory);
    }

    [Fact]
    public async Task S100_correcting_the_last_transcript_supersedes_its_attempts_and_restores_the_level_through_the_engine()
    {
        await using var t = await TestEngine.CreateAsync();
        for (var i = 0; i < 6; i++) await SayAndAttempt(t, "production", "k" + i, "りんごをください", fresh: i < 2);
        await SayAndAttempt(t, "production", "k6", "りんごをください", success: false);
        await SayAndAttempt(t, "production", "k7", "りんごをください", success: false);
        Assert.Equal("n5", t.Engine.GetProfile().WorkingLevel);

        var result = await t.Engine.CorrectLastTranscriptAsync("りんごをどうぞ");
        Assert.Equal("llm_not_configured", result.ErrorCode);
        var profile = t.Engine.GetProfile();
        Assert.True(profile.Evidence[7].Superseded);
        Assert.False(profile.Evidence[6].Superseded);
        Assert.Equal("pre_n5", profile.WorkingLevel);
        Assert.True(profile.LevelHistory.Single().Superseded);
        Assert.Equal("pre_n5", t.Store.Peek("default")!.WorkingLevel);
    }
}

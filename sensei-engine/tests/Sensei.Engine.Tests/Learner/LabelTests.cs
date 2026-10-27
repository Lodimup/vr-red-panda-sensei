using System.Text.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Learner.LearnerKit;

namespace Sensei.Engine.Tests.Learner;

/// <summary>S49 labels, S50 recommendations, S52/S101 review queue, S55 summary rows, S53/S97 query_learner.</summary>
public sealed class LabelTests
{
    private const string I = AttemptOutcomes.IndependentSuccess;
    private const string X = AttemptOutcomes.ConfirmedIncorrect;
    private const string U = AttemptOutcomes.Unresolved;

    private static string LabelOf(params EvidenceRecord[] attempts) => EvidenceRules.ComputeLabels(attempts).Single().Label;

    // ---------------------------------------------------------------- S49

    [Fact]
    public void S49_a_single_independent_success_is_used_independently()
    {
        Assert.Equal("used_independently", LabelOf(Ev(I)));
    }

    [Fact]
    public void S49_independent_successes_in_two_different_sessions_are_stable()
    {
        Assert.Equal("stable", LabelOf(Ev(I, session: "s-1"), Ev(I, session: "s-2")));
    }

    [Fact]
    public void S49_two_independent_successes_in_one_session_are_not_yet_stable()
    {
        Assert.Equal("used_independently", LabelOf(Ev(I, session: "s-1"), Ev(I, session: "s-1")));
    }

    [Fact]
    public void S49_one_independent_success_in_each_of_two_sessions_is_the_boundary_and_a_third_session_changes_nothing()
    {
        Assert.Equal("used_independently", LabelOf(Ev(I, session: "s-2")));
        Assert.Equal("stable", LabelOf(Ev(I, session: "s-1"), Ev(I, session: "s-2"), Ev(I, session: "s-3")));
    }

    [Theory]
    [InlineData(AttemptOutcomes.SuccessAfterModel)]
    [InlineData(AttemptOutcomes.SuccessAfterHint)]
    [InlineData(AttemptOutcomes.SuccessAfterRepetition)]
    [InlineData(AttemptOutcomes.CompletedByDemonstration)]
    public void S49_a_helped_success_is_used_with_help(string outcome)
    {
        Assert.Equal("used_with_help", LabelOf(Ev(outcome)));
    }

    [Fact]
    public void S49_used_independently_outranks_used_with_help()
    {
        Assert.Equal("used_independently", LabelOf(Ev(AttemptOutcomes.SuccessAfterModel), Ev(I), Ev(AttemptOutcomes.SuccessAfterHint)));
    }

    [Fact]
    public void S49_stable_outranks_help_and_later_failures()
    {
        Assert.Equal("stable", LabelOf(Ev(I, session: "s-1"), Ev(AttemptOutcomes.SuccessAfterModel, session: "s-2"), Ev(I, session: "s-2"), Ev(X, session: "s-3")));
    }

    [Fact]
    public void S49_confirmed_incorrect_alone_is_practiced()
    {
        Assert.Equal("practiced", LabelOf(Ev(X)));
    }

    [Fact]
    public void S101_a_target_whose_attempts_are_all_unresolved_is_practiced()
    {
        Assert.Equal("practiced", LabelOf(Ev(U), Ev(U)));
    }

    [Fact]
    public void S49_unresolved_attempts_do_not_count_toward_any_label_above_practiced()
    {
        Assert.Equal("used_independently", LabelOf(Ev(I, session: "s-1"), Ev(U, session: "s-2")));
        Assert.Equal("practiced", LabelOf(Ev(U), Ev(X)));
    }

    [Fact]
    public void S49_superseded_attempts_are_excluded()
    {
        Assert.Equal("used_independently", LabelOf(Ev(I, session: "s-1"), Ev(I, session: "s-2", superseded: true)));
        Assert.Empty(EvidenceRules.ComputeLabels(new[] { Ev(I, superseded: true) }));
    }

    [Fact]
    public void S97_a_target_is_the_pair_dimension_and_key_and_rows_follow_the_first_attempt()
    {
        var labels = EvidenceRules.ComputeLabels(new[]
        {
            Ev(X, "ringo", "production"),
            Ev(I, "mizu", "vocabulary"),
            Ev(I, "ringo", "vocabulary"),
            Ev(I, "ringo", "production"),
        });
        Assert.Equal(new[] { ("ringo", "production"), ("mizu", "vocabulary"), ("ringo", "vocabulary") }, labels.Select(l => (l.Key, l.Dimension)));
        Assert.Equal(new[] { "used_independently", "used_independently", "used_independently" }, labels.Select(l => l.Label));
        Assert.Equal(2, labels[0].Attempts);
        Assert.Equal(I, labels[0].LastOutcome);
    }

    [Fact]
    public void S53_label_rows_carry_attempt_count_and_the_latest_outcome()
    {
        var label = EvidenceRules.ComputeLabels(new[] { Ev(I), Ev(X), Ev(U) }).Single();
        Assert.Equal(3, label.Attempts);
        Assert.Equal(U, label.LastOutcome);
    }

    // ---------------------------------------------------------------- S50

    private static string[] Kinds(params EvidenceRecord[] attempts) => EvidenceRules.ComputeRecommendations(attempts).Select(r => r.Kind).ToArray();

    [Fact]
    public void S50_three_independent_successes_with_a_fresh_variation_step_up()
    {
        Assert.Equal(new[] { "step_up" }, Kinds(Ev(I), Ev(I, fresh: true), Ev(I)));
    }

    [Fact]
    public void S50_three_independent_successes_without_a_fresh_variation_do_not_step_up()
    {
        Assert.Empty(Kinds(Ev(I), Ev(I), Ev(I)));
    }

    [Fact]
    public void S50_two_independent_successes_do_not_step_up()
    {
        Assert.Empty(Kinds(Ev(I, fresh: true), Ev(I, fresh: true)));
    }

    [Fact]
    public void S50_three_of_the_last_four_is_enough_and_a_failure_among_them_is_fine()
    {
        Assert.Equal(new[] { "step_up" }, Kinds(Ev(I), Ev(AttemptOutcomes.SuccessAfterHint), Ev(I, fresh: true), Ev(I)));
    }

    [Fact]
    public void S50_only_the_last_four_resolved_attempts_are_looked_at()
    {
        // the fresh independent success is the fifth from the end
        Assert.Empty(Kinds(Ev(I, fresh: true), Ev(X), Ev(I), Ev(I), Ev(I)));
        Assert.Equal(new[] { "step_up" }, Kinds(Ev(X), Ev(I, fresh: true), Ev(I), Ev(I), Ev(I)));
    }

    [Fact]
    public void S50_unresolved_and_superseded_attempts_do_not_take_a_slot_in_the_window()
    {
        Assert.Equal(new[] { "step_up" }, Kinds(Ev(I, fresh: true), Ev(U), Ev(I), Ev(I, superseded: true), Ev(I)));
    }

    [Fact]
    public void S50_two_confirmed_incorrect_in_the_last_four_offer_something_simpler()
    {
        Assert.Equal(new[] { "offer_simpler" }, Kinds(Ev(X), Ev(X)));
        Assert.Equal(new[] { "offer_simpler" }, Kinds(Ev(X), Ev(I), Ev(AttemptOutcomes.SuccessAfterHint), Ev(X)));
    }

    [Fact]
    public void S50_one_confirmed_incorrect_or_two_outside_the_window_offer_nothing()
    {
        Assert.Empty(Kinds(Ev(X), Ev(I)));
        Assert.Empty(Kinds(Ev(X), Ev(X), Ev(AttemptOutcomes.SuccessAfterHint), Ev(AttemptOutcomes.SuccessAfterHint), Ev(AttemptOutcomes.SuccessAfterHint), Ev(AttemptOutcomes.SuccessAfterHint)));
    }

    [Fact]
    public void S50_recommendations_are_per_target_pair()
    {
        var recs = EvidenceRules.ComputeRecommendations(new[]
        {
            Ev(X, "ringo", "production"), Ev(X, "ringo", "production"),
            Ev(I, "ringo", "vocabulary"), Ev(I, "ringo", "vocabulary"), Ev(I, "ringo", "vocabulary", fresh: true),
        });
        Assert.Equal(new[] { ("ringo", "production", "offer_simpler"), ("ringo", "vocabulary", "step_up") }, recs.Select(r => (r.Key, r.Dimension, r.Kind)));
    }

    // ---------------------------------------------------------------- S52 / S101

    private static string[] Due(string session, params EvidenceRecord[] attempts) => EvidenceRules.ComputeReviewDue(attempts, session).ToArray();

    [Fact]
    public void S52_a_latest_resolved_confirmed_incorrect_is_due_even_for_a_stable_target()
    {
        Assert.Equal(new[] { "k1" }, Due("s-9", Ev(I, session: "s-1"), Ev(I, session: "s-2"), Ev(X, session: "s-3")));
    }

    [Fact]
    public void S52_a_target_that_recovered_after_an_incorrect_attempt_is_not_due_unless_another_rule_applies()
    {
        Assert.Empty(Due("s-9", Ev(X, session: "s-1"), Ev(I, session: "s-1")));
    }

    [Fact]
    public void S52_used_with_help_is_due()
    {
        Assert.Equal(new[] { "k1" }, Due("s-9", Ev(AttemptOutcomes.SuccessAfterHint, session: "s-1")));
    }

    [Fact]
    public void S52_used_independently_is_due_only_within_the_current_session()
    {
        Assert.Equal(new[] { "k1" }, Due("s-2", Ev(I, session: "s-2")));
        Assert.Empty(Due("s-9", Ev(I, session: "s-2")));
    }

    [Fact]
    public void S52_a_target_independent_in_the_current_and_an_earlier_session_is_stable_and_not_due()
    {
        Assert.Empty(Due("s-2", Ev(I, session: "s-1"), Ev(I, session: "s-2")));
    }

    [Fact]
    public void S52_several_independent_successes_all_in_the_current_session_are_due()
    {
        Assert.Equal(new[] { "k1" }, Due("s-1", Ev(I, session: "s-1"), Ev(I, session: "s-1")));
    }

    [Fact]
    public void S101_a_target_whose_latest_attempt_is_unresolved_is_due()
    {
        Assert.Equal(new[] { "k1" }, Due("s-9", Ev(I, session: "s-1"), Ev(U, session: "s-2")));
        Assert.Equal(new[] { "k1" }, Due("s-9", Ev(U)));
    }

    [Fact]
    public void S101_an_unresolved_attempt_that_was_followed_by_a_success_does_not_make_the_target_due_by_itself()
    {
        Assert.Empty(Due("s-9", Ev(U, session: "s-1"), Ev(I, session: "s-1")));
    }

    [Fact]
    public void S52_stable_targets_and_superseded_attempts_are_not_due()
    {
        Assert.Empty(Due("s-9", Ev(I, session: "s-1"), Ev(I, session: "s-2"), Ev(X, session: "s-3", superseded: true)));
    }

    [Fact]
    public void S97_review_due_lists_distinct_keys_in_order_of_first_attempt()
    {
        var due = Due("s-9",
            Ev(X, "zzz", "production"),
            Ev(AttemptOutcomes.SuccessAfterModel, "aaa", "vocabulary"),
            Ev(X, "zzz", "vocabulary"));
        Assert.Equal(new[] { "zzz", "aaa" }, due);
    }

    // ---------------------------------------------------------------- S55 summary rows

    [Fact]
    public void S55_practiced_rows_cover_the_sessions_targets_with_counts_and_labels_from_all_evidence()
    {
        var evidence = new[]
        {
            Ev(I, "ringo", "production", "s-1"),
            Ev(I, "ringo", "production", "s-2"),
            Ev(AttemptOutcomes.SuccessAfterHint, "ringo", "production", "s-2"),
            Ev(U, "ringo", "production", "s-2"),
            Ev(X, "mizu", "production", "s-2"),
            Ev(I, "other", "production", "s-1"),
        };
        var rows = EvidenceRules.BuildPracticed(evidence, "s-2");
        Assert.Equal(new[] { "ringo", "mizu" }, rows.Select(r => r.Key));
        var ringo = rows[0];
        Assert.Equal("stable", ringo.Label);
        Assert.Equal((1, 1, 1), (ringo.Independent, ringo.Supported, ringo.Unresolved));
        var mizu = rows[1];
        Assert.Equal("practiced", mizu.Label);
        Assert.Equal((0, 0, 0), (mizu.Independent, mizu.Supported, mizu.Unresolved));
    }

    [Fact]
    public void S55_a_demonstrated_success_is_supported_not_independent()
    {
        var row = EvidenceRules.BuildPracticed(new[] { Ev(AttemptOutcomes.CompletedByDemonstration, session: "s-1") }, "s-1").Single();
        Assert.Equal((0, 1), (row.Independent, row.Supported));
    }

    [Fact]
    public void S55_superseded_attempts_and_other_sessions_leave_no_row()
    {
        Assert.Empty(EvidenceRules.BuildPracticed(new[] { Ev(I, session: "s-1", superseded: true), Ev(I, session: "s-2") }, "s-1"));
    }

    [Fact]
    public void S55_unresolved_keys_are_keys_whose_latest_attempt_in_the_session_is_unresolved()
    {
        var evidence = new[]
        {
            Ev(U, "a", session: "s-1"),
            Ev(U, "b", session: "s-1"),
            Ev(I, "b", session: "s-1"),
            Ev(U, "c", session: "s-2"),
            Ev(U, "d", session: "s-1", superseded: true),
        };
        Assert.Equal(new[] { "a" }, EvidenceRules.BuildUnresolvedKeys(evidence, "s-1"));
        Assert.Equal(new[] { "c" }, EvidenceRules.BuildUnresolvedKeys(evidence, "s-2"));
    }

    [Fact]
    public void S55_unresolved_keys_are_distinct()
    {
        var evidence = new[] { Ev(U, "a", "production", "s-1"), Ev(U, "a", "vocabulary", "s-1") };
        Assert.Equal(new[] { "a" }, EvidenceRules.BuildUnresolvedKeys(evidence, "s-1"));
    }

    // ---------------------------------------------------------------- S53 / S97 query_learner

    [Fact]
    public async Task S53_query_learner_returns_the_whole_learner_model()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("developing_beginner");
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        await SayAndAttempt(t, "vocabulary", "mizu", "みず", success: false);
        t.Engine.BeginTurn("note");
        await t.CallAsync("remember_note", """{"key":"likes","value":"cats"}""");
        var r = await t.CallAsync("query_learner", "{}");
        Assert.True(r.IsSuccess, r.Detail);
        var d = DataOf(r);
        Assert.Equal("developing_beginner", d.GetProperty("starting_band").GetString());
        Assert.Equal("n4", d.GetProperty("working_level").GetString());
        Assert.True(d.GetProperty("support").GetProperty("show_japanese").GetBoolean());
        Assert.False(d.GetProperty("support").GetProperty("show_romaji").GetBoolean());
        Assert.Equal(JsonValueKind.Array, d.GetProperty("pinned").ValueKind);
        var targets = d.GetProperty("targets").EnumerateArray().ToList();
        Assert.Equal(new[] { "ringo", "mizu" }, targets.Select(x => x.GetProperty("key").GetString()));
        Assert.Equal("production", targets[0].GetProperty("dimension").GetString());
        Assert.Equal("used_independently", targets[0].GetProperty("label").GetString());
        Assert.Equal("independent_success", targets[0].GetProperty("last_outcome").GetString());
        Assert.Equal(1, targets[0].GetProperty("attempts").GetInt32());
        Assert.Equal("practiced", targets[1].GetProperty("label").GetString());
        Assert.Equal("confirmed_incorrect", targets[1].GetProperty("last_outcome").GetString());
        Assert.Equal(new[] { "ringo", "mizu" }, StrArray(d.GetProperty("review_due")));
        Assert.Equal(JsonValueKind.Array, d.GetProperty("recommendations").ValueKind);
        Assert.Equal("cats", d.GetProperty("notes").GetProperty("likes").GetString());
    }

    [Fact]
    public async Task S53_a_fresh_profile_has_a_null_band_no_targets_and_the_initial_level()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("hello");
        var d = DataOf(await t.CallAsync("query_learner", "{}"));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("starting_band").ValueKind);
        Assert.Equal("pre_n5", d.GetProperty("working_level").GetString());
        Assert.Empty(d.GetProperty("targets").EnumerateArray());
        Assert.Empty(d.GetProperty("review_due").EnumerateArray());
        Assert.Empty(d.GetProperty("recommendations").EnumerateArray());
        Assert.Equal(JsonValueKind.Object, d.GetProperty("notes").ValueKind);
    }

    [Fact]
    public async Task S53_target_key_limits_targets_to_that_key_and_an_unknown_key_gives_none()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        await SayAndAttempt(t, "production", "mizu", "みずをください");
        t.Engine.BeginTurn("q");
        var one = DataOf(await t.CallAsync("query_learner", """{"target_key":"mizu"}"""));
        Assert.Equal(new[] { "mizu" }, one.GetProperty("targets").EnumerateArray().Select(x => x.GetProperty("key").GetString()));
        var none = DataOf(await t.CallAsync("query_learner", """{"target_key":"nothing"}"""));
        Assert.Empty(none.GetProperty("targets").EnumerateArray());
    }

    [Fact]
    public async Task S97_target_key_returns_every_dimension_using_that_key()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        await SayAndAttempt(t, "vocabulary", "ringo", "りんご");
        t.Engine.BeginTurn("q");
        var d = DataOf(await t.CallAsync("query_learner", """{"target_key":"ringo"}"""));
        Assert.Equal(new[] { "production", "vocabulary" }, d.GetProperty("targets").EnumerateArray().Select(x => x.GetProperty("dimension").GetString()));
    }

    [Fact]
    public async Task S53_query_learner_never_writes_and_ignores_superseded_attempts()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        lock (t.Core.Sync) t.Core.Learner.Profile.Evidence[0].Superseded = true;
        t.Engine.BeginTurn("q");
        var saves = t.Store.SaveCount;
        var d = DataOf(await t.CallAsync("query_learner", "{}"));
        Assert.Empty(d.GetProperty("targets").EnumerateArray());
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S53_query_learner_rejects_unknown_properties()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("q");
        var r = await t.CallAsync("query_learner", """{"key":"ringo"}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
    }

    [Fact]
    public async Task S49_labels_become_stable_across_two_engine_sessions_on_one_store()
    {
        var store = new InMemoryProfileStore();
        await using (var first = await TestEngine.CreateAsync(store: store))
            await SayAndAttempt(first, "production", "ringo", "りんごをください");
        await using var second = await TestEngine.CreateAsync(store: store);
        await SayAndAttempt(second, "production", "ringo", "りんごをください");
        second.Engine.BeginTurn("q");
        var d = DataOf(await second.CallAsync("query_learner", "{}"));
        Assert.Equal("stable", d.GetProperty("targets")[0].GetProperty("label").GetString());
        Assert.Empty(d.GetProperty("review_due").EnumerateArray());
    }

    [Fact]
    public async Task S52_through_the_tool_a_target_used_independently_this_session_is_due_for_review()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        t.Engine.BeginTurn("q");
        var d = DataOf(await t.CallAsync("query_learner", "{}"));
        Assert.Equal(new[] { "ringo" }, StrArray(d.GetProperty("review_due")));
    }

    [Fact]
    public async Task S50_through_the_tool_three_independent_successes_with_a_fresh_variation_recommend_a_step_up()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        await SayAndAttempt(t, "production", "ringo", "りんごをどうぞ", fresh: true);
        await SayAndAttempt(t, "production", "ringo", "りんごがほしい");
        t.Engine.BeginTurn("q");
        var d = DataOf(await t.CallAsync("query_learner", "{}"));
        var rec = d.GetProperty("recommendations")[0];
        Assert.Equal("ringo", rec.GetProperty("key").GetString());
        Assert.Equal("step_up", rec.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task S103_working_level_is_visible_to_the_model_through_query_learner()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("advanced");
        t.Engine.BeginTurn("q");
        var d = DataOf(await t.CallAsync("query_learner", "{}"));
        Assert.Equal("n2", d.GetProperty("working_level").GetString());
    }
}

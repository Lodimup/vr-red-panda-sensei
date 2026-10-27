using System.Text.Json;
using System.Text.Json.Nodes;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Learner.LearnerKit;

namespace Sensei.Engine.Tests.Learner.ReviewFixes;

/// <summary>S122: the S51 window is anchored on the evidence position stored in <c>attempt_count</c>, never on timestamps.</summary>
public sealed class S122AttemptCountTests
{
    private const string I = AttemptOutcomes.IndependentSuccess;
    private const string X = AttemptOutcomes.ConfirmedIncorrect;

    private static JsonElement StoredHistoryEntry(TestEngine t, int index)
    {
        using var doc = JsonDocument.Parse(t.Store.RawJson("default")!);
        return doc.RootElement.GetProperty("level_history")[index].Clone();
    }

    // ---------------------------------------------------------------- the stored count

    [Fact]
    public async Task S122_a_band_change_stores_the_number_of_evidence_records_at_that_moment()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("early_beginner");
        for (var i = 0; i < 3; i++) Assert.True((await SayAndAttempt(t, "production", "k" + i, "りんご")).IsSuccess);
        await t.Engine.SetStartingBandAsync("intermediate");
        var history = t.Engine.GetProfile().LevelHistory;
        Assert.Equal(new[] { 0, 3 }, history.Select(h => h.AttemptCount));
        Assert.Equal(0, StoredHistoryEntry(t, 0).GetProperty("attempt_count").GetInt32());
        Assert.Equal(3, StoredHistoryEntry(t, 1).GetProperty("attempt_count").GetInt32());
    }

    [Fact]
    public async Task S122_a_set_support_level_change_stores_the_number_of_evidence_records_at_that_moment()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("early_beginner");
        for (var i = 0; i < 2; i++) Assert.True((await SayAndAttempt(t, "production", "k" + i, "りんご")).IsSuccess);
        t.Engine.BeginTurn("harder please");
        var r = await t.CallAsync("set_support", """{"reason":"learner_request","difficulty":"harder"}""");
        Assert.True(r.IsSuccess, r.Detail);
        var last = t.Engine.GetProfile().LevelHistory.Last();
        Assert.Equal("learner_request", last.Reason);
        Assert.Equal(2, last.AttemptCount);
    }

    [Fact]
    public void S122_an_evidence_driven_entry_stores_the_evidence_count_including_the_attempt_that_caused_it()
    {
        var p = NewProfile(WorkingLevels.N3);
        for (var i = 0; i < 6; i++) Add(p, I, fresh: i < 2);
        Add(p, X);
        Add(p, X);
        Assert.Equal(8, p.LevelHistory.Single().AttemptCount);
        for (var i = 0; i < 6; i++) Add(p, I, fresh: i < 2);
        Add(p, X);
        Add(p, X);
        Assert.Equal(new[] { 8, 16 }, p.LevelHistory.Select(h => h.AttemptCount));
    }

    [Fact]
    public void S122_a_replayed_entry_stores_the_position_after_the_last_attempt_it_used()
    {
        var p = NewProfile();
        for (var i = 0; i < 6; i++) Add(p, I, fresh: i < 2);
        Add(p, X);
        Add(p, X);
        for (var i = 0; i < 6; i++) Add(p, I, fresh: i < 2);
        Add(p, X);
        Add(p, X);
        Assert.Equal(new[] { 8, 16 }, p.LevelHistory.Select(h => h.AttemptCount));

        p.Evidence[0].Superseded = true;
        LevelRules.RecomputeAfterSupersede(p, DateTimeOffset.UtcNow);

        Assert.Equal(3, p.LevelHistory.Count);
        // replayed over a2..a9 (positions 1..8): the window closes at the 9th record
        Assert.Equal(9, p.LevelHistory[2].AttemptCount);
        Assert.Equal(WorkingLevels.N5, p.WorkingLevel);
    }

    // ---------------------------------------------------------------- the window

    [Fact]
    public void S122_the_window_starts_at_the_entrys_attempt_count_whatever_its_timestamp_says()
    {
        var p = NewProfile(WorkingLevels.N4);
        for (var i = 0; i < 7; i++) Add(p, I, fresh: i < 2);
        // a manual change stamped long before every attempt: only records after it count
        SupportRules.SetWorkingLevel(p, WorkingLevels.N3, LevelChangeReasons.Band, p.CreatedAt.AddYears(-1));
        Assert.Equal(7, p.LevelHistory.Single().AttemptCount);
        Add(p, I);
        Assert.Equal(WorkingLevels.N3, p.WorkingLevel);
        Assert.Single(p.LevelHistory);
    }

    [Fact]
    public void S122_an_entry_without_attempt_count_reads_as_0_and_a_later_timestamp_does_not_hide_attempts()
    {
        var p = NewProfile(WorkingLevels.N4);
        SupportRules.SetWorkingLevel(p, WorkingLevels.N3, LevelChangeReasons.Band, p.CreatedAt);
        p.LevelHistory[0].At = p.CreatedAt.AddYears(1); // later than every attempt that follows

        var node = JsonSerializer.SerializeToNode(p, SenseiJsonContext.Default.LearnerProfile)!;
        foreach (var entry in node["level_history"]!.AsArray()) entry!.AsObject().Remove("attempt_count");
        var older = JsonSerializer.Deserialize(node.ToJsonString(), SenseiJsonContext.Default.LearnerProfile)!;
        Assert.Equal(0, older.LevelHistory.Single().AttemptCount);

        for (var i = 0; i < 6; i++) Add(older, I, fresh: i < 2);
        Add(older, X);
        Add(older, X);
        Assert.Equal(WorkingLevels.N2, older.WorkingLevel);
        Assert.Equal("evidence_up", older.LevelHistory[1].Reason);
    }

    [Fact]
    public async Task S122_repeated_set_support_level_changes_at_one_instant_each_reset_the_window()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("set the band");
        var first = await t.CallAsync("set_support", """{"reason":"learner_request","starting_band":"early_beginner"}""");
        Assert.True(first.IsSuccess, first.Detail);
        for (var i = 0; i < 4; i++) Assert.True((await SayAndAttempt(t, "production", "before" + i, "りんご", fresh: true)).IsSuccess);
        t.Engine.BeginTurn("set the band again");
        var second = await t.CallAsync("set_support", """{"reason":"learner_request","starting_band":"intermediate"}""");
        Assert.True(second.IsSuccess, second.Detail);
        Assert.Equal("n3", t.Engine.GetProfile().WorkingLevel);
        for (var i = 0; i < 4; i++) Assert.True((await SayAndAttempt(t, "production", "after" + i, "りんご", fresh: true)).IsSuccess);
        Assert.Equal("n3", t.Engine.GetProfile().WorkingLevel);
        Assert.DoesNotContain(t.Engine.GetProfile().LevelHistory, h => h.Reason == "evidence_up");
    }

    // ---------------------------------------------------------------- timestamps are never adjusted

    [Fact]
    public async Task S122_an_attempt_keeps_the_clock_time_right_after_a_level_change_at_that_instant()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("early_beginner");
        var r = await SayAndAttempt(t, "production", "ringo", "りんご");
        Assert.Equal(t.Clock.GetUtcNow(), DataOf(r).GetProperty("at").GetDateTimeOffset());
        Assert.Equal(t.Clock.GetUtcNow(), t.Engine.GetProfile().LevelHistory.Single().At);
    }

    [Fact]
    public async Task S122_a_clock_that_runs_backwards_does_not_change_the_window()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("early_beginner");
        t.Clock.Advance(TimeSpan.FromHours(-2));
        for (var i = 0; i < 6; i++) Assert.True((await SayAndAttempt(t, "production", "k" + i, "りんご", fresh: i < 2)).IsSuccess);
        Assert.True((await SayAndAttempt(t, "production", "k6", "りんご", success: false)).IsSuccess);
        Assert.Equal("n5", t.Engine.GetProfile().WorkingLevel);
        Assert.True((await SayAndAttempt(t, "production", "k7", "りんご", success: false)).IsSuccess);
        Assert.Equal("n4", t.Engine.GetProfile().WorkingLevel);
    }
}

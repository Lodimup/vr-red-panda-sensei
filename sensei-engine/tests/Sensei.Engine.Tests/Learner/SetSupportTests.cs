using System.Text.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Learner.LearnerKit;

namespace Sensei.Engine.Tests.Learner;

/// <summary>set_support: S41, S76, S103, S120.</summary>
public sealed class SetSupportTests
{
    private static async Task<ToolResult> Set(TestEngine t, string json)
    {
        t.Engine.BeginTurn("settings");
        return await t.CallAsync("set_support", json);
    }

    private static List<PreferenceChange> Changes(TestEngine t) =>
        t.Engine.GetProfile().Sessions.Single(s => s.SessionId == t.Engine.SessionId).PreferenceChanges;

    // ---------------------------------------------------------------- arguments

    [Fact]
    public async Task S41_a_call_with_only_a_reason_is_invalid_arguments()
    {
        await using var t = await TestEngine.CreateAsync();
        var saves = t.Store.SaveCount;
        var r = await Set(t, """{"reason":"learner_request"}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Theory]
    [InlineData("""{"show_romaji":false}""")]
    [InlineData("""{"reason":"whim","show_romaji":false}""")]
    [InlineData("""{"reason":"learner_request","pace":"fast"}""")]
    [InlineData("""{"reason":"learner_request","correction_style":"harsh"}""")]
    [InlineData("""{"reason":"learner_request","language_mode":"klingon"}""")]
    [InlineData("""{"reason":"learner_request","difficulty":"hardest"}""")]
    [InlineData("""{"reason":"learner_request","starting_band":"expert"}""")]
    [InlineData("""{"reason":"learner_request","self_reported_jlpt":"n6"}""")]
    [InlineData("""{"reason":"learner_request","self_reported_jlpt":"pre_n5"}""")]
    [InlineData("""{"reason":"learner_request","show_romaji":"no"}""")]
    [InlineData("""{"reason":"learner_request","working_level":"n3"}""")]
    public async Task S41_set_support_arguments_are_strict(string json)
    {
        await using var t = await TestEngine.CreateAsync();
        var saves = t.Store.SaveCount;
        var r = await Set(t, json);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal(saves, t.Store.SaveCount);
        Assert.Empty(Changes(t));
    }

    [Fact]
    public async Task S21_an_unknown_enum_value_lists_the_allowed_values()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Set(t, """{"reason":"learner_request","pace":"fast"}""");
        Assert.Contains("normal", r.Detail);
        Assert.Contains("slow", r.Detail);
    }

    // ---------------------------------------------------------------- S41 plain changes

    [Fact]
    public async Task S41_learner_request_changes_every_support_field()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Set(t, """{"reason":"learner_request","show_japanese":false,"show_reading":false,"show_romaji":false,"show_translation":false,"pace":"slow","correction_style":"detailed","language_mode":"japanese_only"}""");
        Assert.True(r.IsSuccess, r.Detail);
        var s = t.Engine.GetProfile().Support;
        Assert.False(s.ShowJapanese || s.ShowReading || s.ShowRomaji || s.ShowTranslation);
        Assert.Equal(("slow", "detailed", "japanese_only"), (s.Pace, s.CorrectionStyle, s.LanguageMode));
        Assert.Equal(7, Changes(t).Count);
    }

    [Fact]
    public async Task S41_the_result_data_holds_support_pinned_and_the_working_level()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        var r = await Set(t, """{"reason":"learner_request","show_romaji":false}""");
        var d = DataOf(r);
        Assert.False(d.GetProperty("support").GetProperty("show_romaji").GetBoolean());
        Assert.Equal("slow", d.GetProperty("support").GetProperty("pace").GetString());
        Assert.Equal(new[] { "pace" }, StrArray(d.GetProperty("pinned")));
        Assert.Equal("pre_n5", d.GetProperty("working_level").GetString());
        Assert.Empty(r.AffectedIds);
    }

    [Fact]
    public async Task S41_success_saves_the_profile_raises_support_changed_and_records_the_change_as_sensei()
    {
        await using var t = await TestEngine.CreateAsync();
        var saves = t.Store.SaveCount;
        var r = await Set(t, """{"reason":"adaptation","pace":"slow"}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal(saves + 1, t.Store.SaveCount);
        Assert.Equal("slow", t.Store.Peek("default")!.Support.Pace);
        var raised = t.Events.SupportChanges;
        Assert.Single(raised);
        Assert.Equal("slow", raised[0].Pace);
        var change = Changes(t).Single();
        Assert.Equal(("pace", "normal", "slow", "sensei"), (change.Field, change.From, change.To, change.By));
    }

    [Fact]
    public async Task S41_a_value_equal_to_the_current_one_succeeds_without_recording_a_change()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Set(t, """{"reason":"learner_request","pace":"normal"}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Empty(Changes(t));
        Assert.Single(t.Events.SupportChanges);
    }

    [Fact]
    public async Task S76_learner_request_changes_a_field_without_pinning_it()
    {
        await using var t = await TestEngine.CreateAsync();
        await Set(t, """{"reason":"learner_request","show_romaji":false}""");
        Assert.Empty(t.Engine.GetProfile().Pinned);
        var again = await Set(t, """{"reason":"adaptation","show_romaji":true}""");
        Assert.True(again.IsSuccess, again.Detail);
    }

    // ---------------------------------------------------------------- S76 pins

    [Theory]
    [InlineData("learner_request")]
    [InlineData("adaptation")]
    public async Task S76_a_pinned_field_is_refused_whatever_the_reason_and_nothing_changes(string reason)
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = true });
        var saves = t.Store.SaveCount;
        var r = await Set(t, $$"""{"reason":"{{reason}}","show_romaji":false,"pace":"slow"}""");
        Assert.Equal(ToolErrorCodes.PreferencePinned, r.ErrorCode);
        Assert.Contains("show_romaji", r.Detail);
        var s = t.Engine.GetProfile().Support;
        Assert.True(s.ShowRomaji);
        Assert.Equal("normal", s.Pace);
        Assert.Equal(saves, t.Store.SaveCount);
        Assert.Single(t.Events.SupportChanges); // only the player's own pin raised an event
        Assert.Empty(Changes(t));
    }

    [Fact]
    public async Task S76_touching_a_pinned_field_with_its_current_value_is_still_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        var r = await Set(t, """{"reason":"learner_request","pace":"slow"}""");
        Assert.Equal(ToolErrorCodes.PreferencePinned, r.ErrorCode);
    }

    [Fact]
    public async Task S76_an_unpinned_field_next_to_a_pinned_one_is_accepted_when_it_does_not_touch_it()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        var r = await Set(t, """{"reason":"learner_request","show_romaji":false}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal(new[] { "pace" }, t.Engine.GetProfile().Pinned);
    }

    [Fact]
    public async Task S76_pins_made_by_the_player_after_a_tool_change_win_and_stay_pinned_with_the_players_value()
    {
        await using var t = await TestEngine.CreateAsync();
        await Set(t, """{"reason":"learner_request","pace":"slow"}""");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "normal" });
        var r = await Set(t, """{"reason":"learner_request","pace":"slow"}""");
        Assert.Equal(ToolErrorCodes.PreferencePinned, r.ErrorCode);
        Assert.Equal("normal", t.Engine.GetProfile().Support.Pace);
    }

    [Fact]
    public async Task S76_every_support_field_can_be_pinned_against_the_tool()
    {
        foreach (var (field, value) in new[]
                 {
                     ("show_japanese", "false"), ("show_reading", "false"), ("show_romaji", "false"), ("show_translation", "false"),
                     ("pace", "\"slow\""), ("correction_style", "\"detailed\""), ("language_mode", "\"mixed\""),
                 })
        {
            await using var t = await TestEngine.CreateAsync();
            await t.Engine.SetPreferenceByPlayerAsync(PatchFor(field));
            var r = await Set(t, $$"""{"reason":"adaptation","{{field}}":{{value}} }""");
            Assert.Equal(ToolErrorCodes.PreferencePinned, r.ErrorCode);
            Assert.Contains(field, r.Detail);
        }
    }

    private static SupportPatch PatchFor(string field) => field switch
    {
        "show_japanese" => new SupportPatch { ShowJapanese = true },
        "show_reading" => new SupportPatch { ShowReading = true },
        "show_romaji" => new SupportPatch { ShowRomaji = true },
        "show_translation" => new SupportPatch { ShowTranslation = true },
        "pace" => new SupportPatch { Pace = "normal" },
        "correction_style" => new SupportPatch { CorrectionStyle = "light" },
        _ => new SupportPatch { LanguageMode = "english_explanations" },
    };

    // ---------------------------------------------------------------- bands

    [Theory]
    [InlineData("zero", true, true, true, true, "english_explanations", "pre_n5")]
    [InlineData("early_beginner", true, true, true, true, "mixed", "n5")]
    [InlineData("developing_beginner", true, true, false, true, "mixed", "n4")]
    [InlineData("intermediate", true, true, false, false, "mixed", "n3")]
    [InlineData("advanced", true, false, false, false, "japanese_only", "n2")]
    public async Task S41_starting_band_applies_its_row_to_the_display_fields_language_mode_and_the_working_level(
        string band, bool japanese, bool reading, bool romaji, bool translation, string languageMode, string level)
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Set(t, $$"""{"reason":"learner_request","starting_band":"{{band}}"}""");
        Assert.True(r.IsSuccess, r.Detail);
        var p = t.Engine.GetProfile();
        Assert.Equal(band, p.StartingBand);
        Assert.Equal((japanese, reading, romaji, translation, languageMode), (p.Support.ShowJapanese, p.Support.ShowReading, p.Support.ShowRomaji, p.Support.ShowTranslation, p.Support.LanguageMode));
        Assert.Equal(level, p.WorkingLevel);
        Assert.Equal(level, DataOf(r).GetProperty("working_level").GetString());
        Assert.Equal(("normal", "light"), (p.Support.Pace, p.Support.CorrectionStyle));
    }

    [Fact]
    public async Task S41_a_band_that_changes_the_level_appends_a_band_entry_and_records_the_band_and_the_changed_fields()
    {
        await using var t = await TestEngine.CreateAsync();
        await Set(t, """{"reason":"learner_request","starting_band":"advanced"}""");
        var entry = t.Engine.GetProfile().LevelHistory.Single();
        Assert.Equal(("pre_n5", "n2", "band"), (entry.From, entry.To, entry.Reason));
        Assert.Null(entry.EvidenceIds);
        var changes = Changes(t);
        Assert.Equal(new[] { "starting_band", "show_reading", "show_romaji", "show_translation", "language_mode" }, changes.Select(c => c.Field));
        Assert.All(changes, c => Assert.Equal("sensei", c.By));
        Assert.DoesNotContain(changes, c => c.Field.Contains("level"));
    }

    [Fact]
    public async Task S41_a_band_whose_level_equals_the_current_one_appends_no_level_entry()
    {
        await using var t = await TestEngine.CreateAsync();
        await Set(t, """{"reason":"learner_request","starting_band":"zero"}""");
        Assert.Empty(t.Engine.GetProfile().LevelHistory);
        Assert.Equal("zero", t.Engine.GetProfile().StartingBand);
    }

    [Fact]
    public async Task S41_a_band_skips_pinned_display_fields_and_a_pinned_language_mode()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowTranslation = true, LanguageMode = "english_explanations" });
        var r = await Set(t, """{"reason":"learner_request","starting_band":"advanced"}""");
        Assert.True(r.IsSuccess, r.Detail);
        var p = t.Engine.GetProfile();
        Assert.True(p.Support.ShowTranslation);
        Assert.Equal("english_explanations", p.Support.LanguageMode);
        Assert.False(p.Support.ShowReading);
        Assert.False(p.Support.ShowRomaji);
        Assert.Equal("n2", p.WorkingLevel);
    }

    [Fact]
    public async Task S41_learner_request_may_set_the_band_again_and_the_level_follows_the_new_band()
    {
        await using var t = await TestEngine.CreateAsync();
        await Set(t, """{"reason":"learner_request","starting_band":"advanced"}""");
        var r = await Set(t, """{"reason":"learner_request","starting_band":"early_beginner"}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("n5", t.Engine.GetProfile().WorkingLevel);
        Assert.Equal(2, t.Engine.GetProfile().LevelHistory.Count);
    }

    [Fact]
    public async Task S41_adaptation_may_set_the_band_while_it_is_unset()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Set(t, """{"reason":"adaptation","starting_band":"intermediate"}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("intermediate", t.Engine.GetProfile().StartingBand);
    }

    [Fact]
    public async Task S120_adaptation_setting_the_band_while_it_is_already_set_is_band_already_set_and_changes_nothing()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("early_beginner");
        var before = t.Engine.GetProfile();
        var saves = t.Store.SaveCount;
        var r = await Set(t, """{"reason":"adaptation","starting_band":"advanced","pace":"slow"}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("band_already_set", r.Detail);
        var after = t.Engine.GetProfile();
        Assert.Equal(before.StartingBand, after.StartingBand);
        Assert.Equal(before.WorkingLevel, after.WorkingLevel);
        Assert.Equal("normal", after.Support.Pace);
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S120_the_same_band_again_under_adaptation_is_also_band_already_set()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("early_beginner");
        var r = await Set(t, """{"reason":"adaptation","starting_band":"early_beginner"}""");
        Assert.Equal("band_already_set", r.Detail);
    }

    [Fact]
    public async Task S76_the_pin_check_comes_first_when_a_call_is_both_pinned_and_band_already_set()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("early_beginner");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        var r = await Set(t, """{"reason":"adaptation","starting_band":"advanced","pace":"normal"}""");
        Assert.Equal(ToolErrorCodes.PreferencePinned, r.ErrorCode);
    }

    // ---------------------------------------------------------------- self report

    [Fact]
    public async Task S41_a_self_report_under_learner_request_sets_the_level_and_the_self_reported_field()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Set(t, """{"reason":"learner_request","self_reported_jlpt":"n3"}""");
        Assert.True(r.IsSuccess, r.Detail);
        var p = t.Engine.GetProfile();
        Assert.Equal("n3", p.SelfReportedJlpt);
        Assert.Equal("n3", p.WorkingLevel);
        var entry = p.LevelHistory.Single();
        Assert.Equal(("pre_n5", "n3", "self_report"), (entry.From, entry.To, entry.Reason));
        Assert.Empty(Changes(t));
    }

    [Fact]
    public async Task S41_a_self_report_under_adaptation_sets_the_level_while_no_evidence_exists()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Set(t, """{"reason":"adaptation","self_reported_jlpt":"n4"}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("n4", t.Engine.GetProfile().WorkingLevel);
    }

    [Fact]
    public async Task S120_a_self_report_under_adaptation_after_evidence_is_stored_but_leaves_the_level_alone()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        var r = await Set(t, """{"reason":"adaptation","self_reported_jlpt":"n2"}""");
        Assert.True(r.IsSuccess, r.Detail);
        var p = t.Engine.GetProfile();
        Assert.Equal("n2", p.SelfReportedJlpt);
        Assert.Equal("pre_n5", p.WorkingLevel);
        Assert.Empty(p.LevelHistory);
        Assert.Equal("pre_n5", DataOf(r).GetProperty("working_level").GetString());
        Assert.Equal("n2", t.Store.Peek("default")!.SelfReportedJlpt);
    }

    [Fact]
    public async Task S41_a_self_report_under_learner_request_after_evidence_still_sets_the_level()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        var r = await Set(t, """{"reason":"learner_request","self_reported_jlpt":"n2"}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("n2", t.Engine.GetProfile().WorkingLevel);
    }

    [Fact]
    public async Task S41_superseded_evidence_does_not_count_as_evidence_for_a_self_report()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        lock (t.Core.Sync) t.Core.Learner.Profile.Evidence[0].Superseded = true;
        var r = await Set(t, """{"reason":"adaptation","self_reported_jlpt":"n4"}""");
        Assert.Equal("n4", t.Engine.GetProfile().WorkingLevel);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task S41_a_self_report_equal_to_the_current_level_appends_no_entry()
    {
        await using var t = await TestEngine.CreateAsync();
        await Set(t, """{"reason":"learner_request","self_reported_jlpt":"n5"}""");
        await Set(t, """{"reason":"learner_request","self_reported_jlpt":"n5"}""");
        Assert.Single(t.Engine.GetProfile().LevelHistory);
    }

    // ---------------------------------------------------------------- difficulty

    [Theory]
    [InlineData("harder", "n3", "n2")]
    [InlineData("easier", "n3", "n4")]
    [InlineData("harder", "pre_n5", "n5")]
    [InlineData("easier", "n5", "pre_n5")]
    [InlineData("harder", "n2", "n1")]
    public async Task S41_difficulty_moves_the_level_one_step(string difficulty, string from, string to)
    {
        await using var t = await TestEngine.CreateAsync();
        SeedLevel(t, from);
        var r = await Set(t, $$"""{"reason":"learner_request","difficulty":"{{difficulty}}"}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal(to, t.Engine.GetProfile().WorkingLevel);
        var entry = t.Engine.GetProfile().LevelHistory.Last();
        Assert.Equal((from, to, "learner_request"), (entry.From, entry.To, entry.Reason));
    }

    [Theory]
    [InlineData("harder", "n1")]
    [InlineData("easier", "pre_n5")]
    public async Task S41_difficulty_is_clamped_at_the_ends_and_adds_no_entry(string difficulty, string level)
    {
        await using var t = await TestEngine.CreateAsync();
        SeedLevel(t, level);
        var historyBefore = t.Engine.GetProfile().LevelHistory.Count;
        var r = await Set(t, $$"""{"reason":"adaptation","difficulty":"{{difficulty}}"}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal(level, t.Engine.GetProfile().WorkingLevel);
        Assert.Equal(historyBefore, t.Engine.GetProfile().LevelHistory.Count);
    }

    [Fact]
    public async Task S41_difficulty_under_adaptation_records_the_adaptation_reason()
    {
        await using var t = await TestEngine.CreateAsync();
        SeedLevel(t, "n4");
        await Set(t, """{"reason":"adaptation","difficulty":"easier"}""");
        Assert.Equal("adaptation", t.Engine.GetProfile().LevelHistory.Last().Reason);
    }

    private static void SeedLevel(TestEngine t, string level)
    {
        lock (t.Core.Sync) t.Core.Learner.Profile.WorkingLevel = level;
    }

    // ---------------------------------------------------------------- S103 order

    [Fact]
    public async Task S103_band_then_explicit_fields_then_self_report_then_difficulty_each_with_its_own_entry()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Set(t, """{"reason":"learner_request","difficulty":"easier","self_reported_jlpt":"n5","language_mode":"japanese_only","show_translation":true,"starting_band":"intermediate"}""");
        Assert.True(r.IsSuccess, r.Detail);
        var p = t.Engine.GetProfile();
        Assert.Equal(new[] { "band", "self_report", "learner_request" }, p.LevelHistory.Select(h => h.Reason));
        Assert.Equal(new[] { ("pre_n5", "n3"), ("n3", "n5"), ("n5", "pre_n5") }, p.LevelHistory.Select(h => (h.From, h.To)));
        Assert.Equal("pre_n5", p.WorkingLevel);
        // explicit fields override the band's defaults
        Assert.Equal("japanese_only", p.Support.LanguageMode);
        Assert.True(p.Support.ShowTranslation);
        Assert.Equal("n5", p.SelfReportedJlpt);
        Assert.Equal("intermediate", p.StartingBand);
    }

    [Fact]
    public async Task S103_difficulty_applies_to_the_level_reached_by_the_band_and_self_report()
    {
        await using var t = await TestEngine.CreateAsync();
        await Set(t, """{"reason":"learner_request","starting_band":"advanced","difficulty":"harder"}""");
        Assert.Equal("n1", t.Engine.GetProfile().WorkingLevel);
        Assert.Equal(new[] { "band", "learner_request" }, t.Engine.GetProfile().LevelHistory.Select(h => h.Reason));
    }

    [Fact]
    public async Task S103_a_refused_call_changes_no_part_of_it()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        var before = JsonSerializer.Serialize(t.Engine.GetProfile(), Sensei.Engine.Json.SenseiJsonContext.Default.LearnerProfileView);
        var r = await Set(t, """{"reason":"learner_request","starting_band":"advanced","difficulty":"harder","self_reported_jlpt":"n1","pace":"normal"}""");
        Assert.Equal(ToolErrorCodes.PreferencePinned, r.ErrorCode);
        var after = JsonSerializer.Serialize(t.Engine.GetProfile(), Sensei.Engine.Json.SenseiJsonContext.Default.LearnerProfileView);
        Assert.Equal(before, after);
    }
}

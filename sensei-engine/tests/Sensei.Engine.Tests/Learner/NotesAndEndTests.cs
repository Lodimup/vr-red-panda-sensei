using System.Reflection;
using System.Text.Json;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.Learner.LearnerKit;

namespace Sensei.Engine.Tests.Learner;

/// <summary>remember_note (S54, S77) and end_activity (S55, S77, S80, S88), plus the hidden working level staying hidden.</summary>
public sealed class NotesAndEndTests
{
    private static async Task<ToolResult> Note(TestEngine t, string key, string value)
    {
        t.Engine.BeginTurn("note");
        return await t.CallAsync("remember_note", new System.Text.Json.Nodes.JsonObject { ["key"] = key, ["value"] = value }.ToJsonString());
    }

    private static Dictionary<string, string> Notes(TestEngine t) => Profile(t).Notes;

    // ---------------------------------------------------------------- S54

    [Fact]
    public async Task S54_a_note_is_stored_saved_and_reported()
    {
        await using var t = await TestEngine.CreateAsync();
        var saves = t.Store.SaveCount;
        var r = await Note(t, "favourite_food", "ramen");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("ramen", Notes(t)["favourite_food"]);
        Assert.Equal(saves + 1, t.Store.SaveCount);
        Assert.Equal("ramen", t.Store.Peek("default")!.Notes["favourite_food"]);
        Assert.Equal("favourite_food", Str(r, "key"));
        Assert.Equal(1, DataOf(r).GetProperty("notes_count").GetInt32());
        Assert.Empty(r.AffectedIds);
    }

    [Fact]
    public async Task S54_the_same_key_replaces_the_note()
    {
        await using var t = await TestEngine.CreateAsync();
        await Note(t, "goal", "travel");
        var r = await Note(t, "goal", "work");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("work", Notes(t)["goal"]);
        Assert.Single(Notes(t));
    }

    [Fact]
    public async Task S54_thirty_distinct_keys_fit_and_the_thirty_first_is_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        for (var i = 0; i < 30; i++) Assert.True((await Note(t, "k" + i, "v")).IsSuccess);
        var saves = t.Store.SaveCount;
        var refused = await Note(t, "k30", "v");
        Assert.Equal(ToolStatuses.Failure, refused.Status);
        Assert.Equal(ToolErrorCodes.NoteLimitExceeded, refused.ErrorCode);
        Assert.Equal(30, Notes(t).Count);
        Assert.DoesNotContain("k30", Notes(t).Keys);
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S54_at_the_limit_an_existing_key_can_still_be_replaced()
    {
        await using var t = await TestEngine.CreateAsync();
        for (var i = 0; i < 30; i++) await Note(t, "k" + i, "v");
        var r = await Note(t, "k7", "changed");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("changed", Notes(t)["k7"]);
        Assert.Equal(30, Notes(t).Count);
    }

    [Fact]
    public async Task S54_the_limit_check_comes_before_the_credential_check()
    {
        await using var t = await TestEngine.CreateAsync();
        for (var i = 0; i < 30; i++) await Note(t, "k" + i, "v");
        var r = await Note(t, "extra", "password: hunter22x");
        Assert.Equal(ToolErrorCodes.NoteLimitExceeded, r.ErrorCode);
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("goal_1", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz_abcde", true)]   // 32 characters: the longest allowed
    [InlineData("abcdefghijklmnopqrstuvwxyz_abcdef", false)] // 33
    [InlineData("", false)]
    [InlineData("Goal", false)]
    [InlineData("1goal", false)]
    [InlineData("_goal", false)]
    [InlineData("go-al", false)]
    [InlineData("go al", false)]
    [InlineData("goal\n", false)]
    public async Task S54_the_key_must_match_the_pattern(string key, bool valid)
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Note(t, key, "value");
        if (valid) Assert.True(r.IsSuccess, r.Detail);
        else
        {
            Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
            Assert.Empty(Notes(t));
        }
    }

    [Fact]
    public async Task S54_the_value_is_1_to_120_characters()
    {
        await using var t = await TestEngine.CreateAsync();
        Assert.True((await Note(t, "a", "x")).IsSuccess);
        Assert.True((await Note(t, "b", new string('あ', 120))).IsSuccess);
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await Note(t, "c", "")).ErrorCode);
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await Note(t, "d", new string('あ', 121))).ErrorCode);
    }

    [Fact]
    public async Task S54_unknown_or_missing_properties_are_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("n");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("remember_note", """{"key":"a"}""")).ErrorCode);
        t.Engine.BeginTurn("n");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("remember_note", """{"key":"a","value":"b","x":1}""")).ErrorCode);
    }

    // ---------------------------------------------------------------- S77 notes

    [Theory]
    [InlineData("sk-abcdefghijklmnop1234")]
    [InlineData("my password: hunter22x")]
    [InlineData("api_key=zzzzzz-1234567")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789abcd")]
    public async Task S77_a_value_that_looks_like_a_credential_is_refused_and_not_stored(string value)
    {
        await using var t = await TestEngine.CreateAsync();
        var saves = t.Store.SaveCount;
        var r = await Note(t, "secret", value);
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, r.ErrorCode);
        Assert.Empty(Notes(t));
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S77_a_key_that_looks_like_a_credential_is_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Note(t, "a1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "value");
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, r.ErrorCode);
        Assert.Empty(Notes(t));
    }

    [Fact]
    public async Task S77_a_known_secret_in_a_value_is_refused_and_never_reaches_the_store()
    {
        const string secret = "zq-example-secret-0009";
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { secret });
        var r = await Note(t, "oops", "my key is " + secret);
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, r.ErrorCode);
        Assert.DoesNotContain(secret, t.Store.RawJson("default"));
        Assert.DoesNotContain(secret, r.Detail ?? "");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("API key")]
    [InlineData("antidisestablishmentarianism")]
    [InlineData("今日はpasswordという単語を練習したい")]
    public async Task S78_ordinary_text_that_mentions_credential_words_is_a_valid_note(string value)
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await Note(t, "word", value);
        Assert.True(r.IsSuccess, r.Detail);
    }

    // ---------------------------------------------------------------- S55 end_activity

    private static async Task<ToolResult> End(TestEngine t, string suggestion = "Practice colours next time.")
    {
        t.Engine.BeginTurn("bye");
        return await t.CallAsync("end_activity", new System.Text.Json.Nodes.JsonObject { ["next_suggestion"] = suggestion }.ToJsonString());
    }

    [Fact]
    public async Task S55_end_activity_ends_the_active_task()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        var (task, _) = StartTask(t, "production", "ringo");
        var r = await t.CallAsync("end_activity", """{"next_suggestion":"Try colours."}""");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Null(t.Engine.GetActiveTask());
        lock (t.Core.Sync)
        {
            Assert.Equal(TaskStatuses.Ended, t.Core.Tasks.Find(task.TaskId)!.Status);
            Assert.Equal(SceneEventKinds.TaskEnded, t.Core.World.Events.Last().Kind);
        }
        Assert.Equal(TaskStatuses.Ended, t.Events.Tasks.Last().Status);
    }

    [Fact]
    public async Task S55_end_activity_without_a_task_still_succeeds()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await End(t);
        Assert.True(r.IsSuccess, r.Detail);
        lock (t.Core.Sync) Assert.Empty(t.Core.World.Events);
    }

    [Fact]
    public async Task S55_the_summary_is_built_from_this_sessions_committed_evidence()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        await SayAndAttempt(t, "production", "mizu", "みずをください", success: false);
        await SayAndAttempt(t, "vocabulary", "kasa", "かさ", assessment: "unclear_input");
        var r = await End(t, "Practise umbrellas.");
        Assert.True(r.IsSuccess, r.Detail);
        var d = DataOf(r);
        Assert.Equal(t.Engine.SessionId, d.GetProperty("session_id").GetString());
        var rows = d.GetProperty("practiced").EnumerateArray().ToList();
        Assert.Equal(new[] { "ringo", "mizu", "kasa" }, rows.Select(x => x.GetProperty("key").GetString()));
        Assert.Equal("used_independently", rows[0].GetProperty("label").GetString());
        Assert.Equal(1, rows[0].GetProperty("independent").GetInt32());
        Assert.Equal(0, rows[0].GetProperty("supported").GetInt32());
        Assert.Equal(1, rows[2].GetProperty("unresolved").GetInt32());
        Assert.Equal(new[] { "kasa" }, StrArray(d.GetProperty("unresolved_keys")));
        Assert.Equal("Practise umbrellas.", d.GetProperty("next_suggestion").GetString());
        Assert.Empty(d.GetProperty("preference_changes").EnumerateArray());
    }

    [Fact]
    public async Task S55_evidence_from_an_earlier_session_is_not_in_this_sessions_summary_but_feeds_its_labels()
    {
        var store = new InMemoryProfileStore();
        await using (var first = await TestEngine.CreateAsync(store: store))
            await SayAndAttempt(first, "production", "ringo", "りんごをください");
        await using var second = await TestEngine.CreateAsync(store: store);
        await SayAndAttempt(second, "production", "ringo", "りんごをください");
        await SayAndAttempt(second, "production", "mizu", "みずをください");
        var d = DataOf(await End(second));
        var rows = d.GetProperty("practiced").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("stable", rows[0].GetProperty("label").GetString());
        Assert.Equal(1, rows[0].GetProperty("independent").GetInt32());
    }

    [Fact]
    public async Task S55_the_summary_lists_the_preference_changes_of_the_session()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("x");
        await t.CallAsync("set_support", """{"reason":"learner_request","pace":"slow"}""");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = false });
        var d = DataOf(await End(t));
        var changes = d.GetProperty("preference_changes").EnumerateArray().ToList();
        Assert.Equal(new[] { ("pace", "normal", "slow", "sensei"), ("show_romaji", "true", "false", "player") },
            changes.Select(c => (c.GetProperty("field").GetString()!, c.GetProperty("from").GetString()!, c.GetProperty("to").GetString()!, c.GetProperty("by").GetString()!)));
    }

    [Fact]
    public async Task S55_end_activity_raises_on_summary_once_with_the_summary_saves_and_remembers_the_suggestion()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        var saves = t.Store.SaveCount;
        var r = await End(t, "Try the verbs.");
        var raised = t.Events.Summaries.Single();
        Assert.Equal("Try the verbs.", raised.NextSuggestion);
        Assert.Equal(t.Engine.SessionId, raised.SessionId);
        Assert.Equal("ringo", raised.Practiced.Single().Key);
        Assert.Equal(saves + 1, t.Store.SaveCount);
        Assert.Equal("Try the verbs.", t.Store.Peek("default")!.Sessions.Single(s => s.SessionId == t.Engine.SessionId).NextSuggestion);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task S55_the_latest_next_suggestion_wins_and_get_session_summary_reports_it_without_ending_anything()
    {
        await using var t = await TestEngine.CreateAsync();
        await End(t, "First idea.");
        await End(t, "Second idea.");
        var summary = t.Engine.GetSessionSummary();
        Assert.Equal("Second idea.", summary.NextSuggestion);
        Assert.Equal(2, t.Events.Summaries.Count);
        _ = t.Engine.GetSessionSummary();
        Assert.Equal(2, t.Events.Summaries.Count);
    }

    [Fact]
    public async Task S55_get_session_summary_before_any_end_activity_has_a_null_suggestion_and_ends_no_task()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        StartTask(t, "production", "ringo");
        var summary = t.Engine.GetSessionSummary();
        Assert.Null(summary.NextSuggestion);
        Assert.NotNull(t.Engine.GetActiveTask());
        Assert.Empty(t.Events.Summaries);
    }

    [Fact]
    public async Task S55_free_conversation_resumes_on_the_next_turn_with_a_fresh_tool_budget()
    {
        await using var t = await TestEngine.CreateAsync();
        await End(t);
        t.Engine.BeginTurn("again");
        var r = await t.CallAsync("query_learner", "{}");
        Assert.True(r.IsSuccess, r.Detail);
    }

    // ---------------------------------------------------------------- S88

    [Fact]
    public async Task S88_after_end_activity_later_calls_of_the_same_generation_are_cancelled_and_change_nothing()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("bye");
        var end = await t.CallAsync("end_activity", """{"next_suggestion":"Rest."}""");
        Assert.True(end.IsSuccess, end.Detail);
        var saves = t.Store.SaveCount;
        var note = await t.CallAsync("remember_note", """{"key":"late","value":"x"}""");
        Assert.Equal(ToolStatuses.Cancelled, note.Status);
        Assert.Equal(ToolErrorCodes.Cancelled, note.ErrorCode);
        Assert.Empty(Notes(t));
        Assert.Equal(saves, t.Store.SaveCount);
        var query = await t.CallAsync("query_learner", "{}");
        Assert.Equal(ToolStatuses.Cancelled, query.Status);
    }

    [Fact]
    public async Task S88_a_failed_end_activity_does_not_end_the_generation()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("bye");
        var refused = await t.CallAsync("end_activity", """{"next_suggestion":"password: hunter22x"}""");
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, refused.ErrorCode);
        var after = await t.CallAsync("query_learner", "{}");
        Assert.True(after.IsSuccess, after.Detail);
    }

    // ---------------------------------------------------------------- S77 / S80 next_suggestion

    [Theory]
    [InlineData("sk-abcdefghijklmnop1234")]
    [InlineData("token=abcdef123456")]
    public async Task S77_a_next_suggestion_that_looks_like_a_credential_is_refused_and_nothing_ends(string suggestion)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        StartTask(t, "production", "ringo");
        var saves = t.Store.SaveCount;
        var r = await t.CallAsync("end_activity", new System.Text.Json.Nodes.JsonObject { ["next_suggestion"] = suggestion }.ToJsonString());
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, r.ErrorCode);
        Assert.NotNull(t.Engine.GetActiveTask());
        Assert.Empty(t.Events.Summaries);
        Assert.Equal(saves, t.Store.SaveCount);
        Assert.Null(t.Engine.GetSessionSummary().NextSuggestion);
    }

    [Fact]
    public async Task S77_a_known_secret_in_the_next_suggestion_is_refused()
    {
        const string secret = "zq-example-secret-0011";
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { secret });
        var r = await End(t, "say " + secret);
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, r.ErrorCode);
        Assert.DoesNotContain(secret, t.Store.RawJson("default"));
    }

    [Theory]
    [InlineData("Your working level is high")]
    [InlineData("Check the Working Level next time")]
    [InlineData("see working_level")]
    [InlineData("WORKING_LEVEL")]
    [InlineData("Try more N5 vocabulary next time")]
    [InlineData("JLPT style listening practice")]
    public async Task S80_a_next_suggestion_that_mentions_the_working_level_is_invalid_arguments_with_hidden_level(string suggestion)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        StartTask(t, "production", "ringo");
        var r = await t.CallAsync("end_activity", new System.Text.Json.Nodes.JsonObject { ["next_suggestion"] = suggestion }.ToJsonString());
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("hidden_level", r.Detail);
        Assert.NotNull(t.Engine.GetActiveTask());
        Assert.Empty(t.Events.Summaries);
    }

    [Theory]
    [InlineData("A working lunch phrase")]
    [InlineData("Try another beginner word.")]
    public async Task S80_ordinary_suggestions_stay_allowed(string suggestion)
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await End(t, suggestion);
        Assert.True(r.IsSuccess, r.Detail);
    }

    [Fact]
    public async Task S55_next_suggestion_is_1_to_160_characters()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("bye");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("end_activity", """{"next_suggestion":""}""")).ErrorCode);
        t.Engine.BeginTurn("bye");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("end_activity", "{}")).ErrorCode);
        t.Engine.BeginTurn("bye");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("end_activity", new System.Text.Json.Nodes.JsonObject { ["next_suggestion"] = new string('あ', 161) }.ToJsonString())).ErrorCode);
        Assert.True((await End(t, new string('あ', 160))).IsSuccess);
    }

    // ---------------------------------------------------------------- the hidden level stays hidden

    [Fact]
    public async Task S80_the_session_summary_and_end_activity_never_contain_the_working_level()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("advanced");
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        await SayAndAttempt(t, "production", "mizu", "みずをください", success: false);
        var end = await End(t, "Practise a new topic.");
        var fromTool = end.Data!.Value.GetRawText();
        var fromEngine = JsonSerializer.Serialize(t.Engine.GetSessionSummary(), SenseiJsonContext.Default.SessionSummary);
        var fromEvent = JsonSerializer.Serialize(t.Events.Summaries.Single(), SenseiJsonContext.Default.SessionSummary);
        foreach (var json in new[] { fromTool, fromEngine, fromEvent })
        {
            Assert.DoesNotContain("level", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("jlpt", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("n2", json);
            Assert.DoesNotContain("pre_n5", json);
        }
        Assert.Equal("n2", t.Engine.GetProfile().WorkingLevel);
    }

    [Fact]
    public void S80_the_summary_types_have_no_level_property()
    {
        foreach (var type in new[] { typeof(SessionSummary), typeof(PracticedTarget), typeof(PreferenceChange) })
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), p => p.Name.Contains("Level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task S80_changing_the_band_or_the_level_never_shows_up_as_a_preference_change_named_after_the_level()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("x");
        await t.CallAsync("set_support", """{"reason":"learner_request","starting_band":"advanced","difficulty":"harder","self_reported_jlpt":"n3"}""");
        var summary = t.Engine.GetSessionSummary();
        Assert.DoesNotContain(summary.PreferenceChanges, c => c.Field.Contains("level", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(summary.PreferenceChanges, c => (c.From ?? "").Contains("n3") || (c.To ?? "").Contains("n3"));
    }

    [Fact]
    public async Task S80_the_working_level_is_visible_to_the_model_in_query_learner_and_set_support()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("x");
        var set = await t.CallAsync("set_support", """{"reason":"learner_request","self_reported_jlpt":"n4"}""");
        Assert.Equal("n4", DataOf(set).GetProperty("working_level").GetString());
        var query = await t.CallAsync("query_learner", "{}");
        Assert.Equal("n4", DataOf(query).GetProperty("working_level").GetString());
    }
}

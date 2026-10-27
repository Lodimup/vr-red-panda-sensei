using System.Text.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;
using static Sensei.Engine.Tests.Learner.LearnerKit;

namespace Sensei.Engine.Tests.Learner;

/// <summary>record_attempt: S43, S46, S47, S77, S93, S95, S96, S98.</summary>
public sealed class RecordAttemptTests
{
    private static long MoveEventId(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.World.Events.Last(e => e.Kind == SceneEventKinds.Moved).EventId;
    }

    // ---------------------------------------------------------------- S43 excerpt

    [Fact]
    public async Task S43_an_excerpt_in_the_current_turns_transcript_is_accepted()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("l1", Str(r, "source_utterance_id"));
    }

    [Fact]
    public async Task S43_an_excerpt_in_the_previous_turns_transcript_is_accepted()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        t.Engine.BeginTurn("はい");
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("l1", Str(r, "source_utterance_id"));
    }

    [Fact]
    public async Task S43_an_excerpt_from_two_turns_ago_is_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        t.Engine.BeginTurn("はい");
        t.Engine.BeginTurn("いいえ");
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal(ToolErrorCodes.ExcerptNotInTranscript, r.ErrorCode);
    }

    [Fact]
    public async Task S43_an_excerpt_not_in_any_transcript_is_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "みずをください");
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.ExcerptNotInTranscript, r.ErrorCode);
    }

    [Theory]
    [InlineData("I want an Apple please", "apple")]
    [InlineData("I want an apple please", "APPLE PLEASE")]
    [InlineData("i   want    an apple", "want an  apple")]
    [InlineData("ＡＢＣ apple", "abc")]
    [InlineData("abc apple", "ＡＢＣ")]
    [InlineData("tab\tseparated\nwords", "tab separated words")]
    public async Task S43_comparison_ignores_case_width_and_whitespace_runs(string said, string excerpt)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn(said);
        var r = await Attempt(t, "production", "apple", excerpt);
        Assert.True(r.IsSuccess, r.Detail);
    }

    [Fact]
    public async Task S43_an_excerpt_that_is_only_whitespace_matches_nothing()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "   ");
        Assert.Equal(ToolErrorCodes.ExcerptNotInTranscript, r.ErrorCode);
    }

    [Fact]
    public async Task S92_a_synthetic_cli_utterance_is_never_an_excerpt_source()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("(cli)");
        var r = await Attempt(t, "production", "ringo", "(cli)");
        Assert.Equal(ToolErrorCodes.ExcerptNotInTranscript, r.ErrorCode);
    }

    [Fact]
    public async Task S93_the_source_is_the_most_recent_utterance_containing_the_excerpt()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんご");
        t.Engine.BeginTurn("りんご、りんご");
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal("l2", Str(r, "source_utterance_id"));
    }

    [Fact]
    public async Task S93_a_superseded_utterance_is_not_a_source()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        lock (t.Core.Sync) t.Core.Utterances.All[0].Superseded = true;
        t.Engine.BeginTurn("みずをください");
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal(ToolErrorCodes.ExcerptNotInTranscript, r.ErrorCode);
    }

    // ---------------------------------------------------------------- S22/S43 failure writes nothing

    [Fact]
    public async Task S22_a_refused_attempt_writes_no_evidence_and_no_profile_save()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var saves = t.Store.SaveCount;
        var r = await Attempt(t, "production", "ringo", "みずをください");
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Empty(Profile(t).Evidence);
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S46_a_committed_attempt_is_saved_to_the_store_at_once()
    {
        await using var t = await TestEngine.CreateAsync();
        var saves = t.Store.SaveCount;
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.Equal(saves + 1, t.Store.SaveCount);
        var stored = t.Store.Peek("default")!;
        Assert.Single(stored.Evidence);
        Assert.Equal("a1", stored.Evidence[0].AttemptId);
    }

    // ---------------------------------------------------------------- S43/S95 events

    [Fact]
    public async Task S43_an_unknown_event_id_is_unknown_event()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "りんご", eventIds: new long[] { 999 });
        Assert.Equal(ToolErrorCodes.UnknownEvent, r.ErrorCode);
        Assert.Empty(Profile(t).Evidence);
    }

    [Fact]
    public async Task S43_event_id_zero_and_negative_are_unknown_events()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        Assert.Equal(ToolErrorCodes.UnknownEvent, (await Attempt(t, "production", "ringo", "りんご", eventIds: new long[] { 0 })).ErrorCode);
        t.Engine.BeginTurn("りんごをください");
        Assert.Equal(ToolErrorCodes.UnknownEvent, (await Attempt(t, "production", "ringo", "りんご", eventIds: new long[] { -3 })).ErrorCode);
    }

    [Fact]
    public async Task S95_a_non_listening_attempt_may_cite_sensei_events_and_actors_follow_the_event_ids()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var apple = Spawn(t, "apple");
        var spawned = LastEventId(t);
        var r = await Attempt(t, "production", "apple", "りんご", eventIds: new[] { spawned });
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal(new[] { "sensei" }, StrArray(DataOf(r).GetProperty("actors")));
        Assert.Equal(spawned, DataOf(r).GetProperty("event_ids")[0].GetInt64());
        _ = apple;
    }

    [Fact]
    public async Task S120_actors_hold_one_entry_per_cited_event_id_in_the_same_order()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var apple = Spawn(t, "apple");
        var spawned = LastEventId(t);
        t.Engine.PlayerMove(apple, "left", null);
        var moved = MoveEventId(t);
        var r = await Attempt(t, "production", "apple", "りんご", eventIds: new[] { moved, spawned });
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal(new[] { "player", "sensei" }, StrArray(DataOf(r).GetProperty("actors")));
        var r2Turn = "りんごをください";
        t.Engine.BeginTurn(r2Turn);
        var same = await Attempt(t, "production", "apple", "りんご", eventIds: new[] { spawned, moved, spawned });
        Assert.Equal(new[] { spawned, moved }, DataOf(same).GetProperty("event_ids").EnumerateArray().Select(x => x.GetInt64()));
        Assert.Equal(new[] { "sensei", "player" }, StrArray(DataOf(same).GetProperty("actors")));
    }

    [Fact]
    public async Task S95_a_non_listening_attempt_without_an_excerpt_is_invalid_arguments()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", null);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.StartsWith("transcript_excerpt", r.Detail);
    }

    [Fact]
    public async Task S95_a_listening_attempt_without_events_is_invalid_arguments()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("ここ");
        var r = await Attempt(t, "listening", "ringo", "ここ");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("listening_requires_player_events", r.Detail);
    }

    [Fact]
    public async Task S95_a_listening_attempt_citing_a_sensei_event_is_invalid_arguments()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("ここ");
        StartTask(t, "listening", "ringo");
        var senseiEvent = LastEventId(t);
        var r = await Attempt(t, "listening", "ringo", "ここ", eventIds: new[] { senseiEvent });
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("listening_requires_player_events", r.Detail);
    }

    [Fact]
    public async Task S95_a_listening_attempt_citing_a_player_event_after_the_task_started_is_accepted_without_an_excerpt()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        var (_, apple) = StartTask(t, "listening", "ringo");
        t.Engine.PlayerMove(apple, "left", null);
        var moved = MoveEventId(t);
        var r = await Attempt(t, "listening", "ringo", null, eventIds: new[] { moved });
        Assert.True(r.IsSuccess, r.Detail);
        Assert.False(DataOf(r).TryGetProperty("source_utterance_id", out _));
        Assert.Equal(new[] { "player" }, StrArray(DataOf(r).GetProperty("actors")));
        Assert.Equal("task_completed", Str(r, "engine_outcome"));
    }

    [Fact]
    public async Task S95_a_listening_attempt_citing_a_player_event_from_before_the_task_started_is_invalid_arguments()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        var apple = Spawn(t, "apple");
        t.Engine.PlayerMove(apple, "right", null);
        var early = MoveEventId(t);
        lock (t.Core.Sync)
            t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "x" }, new[] { new SkillTarget { Dimension = "listening", Key = "ringo" } },
                new[] { new TaskPredicate { Type = PredicateTypes.InRegion, ObjectId = apple, Region = "left" } }, Array.Empty<ScenarioProperty>(), null, false, t.Core.CurrentTurnId);
        var r = await Attempt(t, "listening", "ringo", null, eventIds: new[] { early });
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("listening_requires_player_events", r.Detail);
    }

    [Fact]
    public async Task S95_a_listening_excerpt_that_is_not_in_the_transcript_is_still_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        var (_, apple) = StartTask(t, "listening", "ringo");
        t.Engine.PlayerMove(apple, "left", null);
        var r = await Attempt(t, "listening", "ringo", "nothing like it", eventIds: new[] { MoveEventId(t) });
        Assert.Equal(ToolErrorCodes.ExcerptNotInTranscript, r.ErrorCode);
    }

    [Fact]
    public async Task S95_a_non_listening_event_from_before_the_bound_task_started_is_out_of_window()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var apple = Spawn(t, "apple");
        var early = LastEventId(t);
        lock (t.Core.Sync)
            t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "x" }, new[] { new SkillTarget { Dimension = "production", Key = "ringo" } },
                new[] { new TaskPredicate { Type = PredicateTypes.InRegion, ObjectId = apple, Region = "left" } }, Array.Empty<ScenarioProperty>(), null, false, t.Core.CurrentTurnId);
        var r = await Attempt(t, "production", "ringo", "りんご", eventIds: new[] { early });
        Assert.Equal(ToolErrorCodes.UnknownEvent, r.ErrorCode);
        Assert.Equal("event_out_of_window", r.Detail);
    }

    [Fact]
    public async Task S95_the_task_started_event_itself_is_inside_the_window()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (task, _) = StartTask(t, "production", "ringo");
        var r = await Attempt(t, "production", "ringo", "りんご", eventIds: new[] { task.StartEventId });
        Assert.True(r.IsSuccess, r.Detail);
    }

    [Fact]
    public async Task S95_without_a_task_an_event_from_before_the_previous_learner_utterance_is_out_of_window()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("zero");
        Spawn(t, "apple");
        var early = LastEventId(t);
        t.Engine.BeginTurn("one");
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "りんご", eventIds: new[] { early });
        Assert.Equal(ToolErrorCodes.UnknownEvent, r.ErrorCode);
        Assert.Equal("event_out_of_window", r.Detail);
    }

    [Fact]
    public async Task S95_without_a_task_an_event_after_the_previous_learner_utterance_is_accepted()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("one");
        Spawn(t, "apple");
        var recent = LastEventId(t);
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "りんご", eventIds: new[] { recent });
        Assert.True(r.IsSuccess, r.Detail);
    }

    // ---------------------------------------------------------------- S46 record

    [Fact]
    public async Task S46_the_evidence_record_carries_every_field()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (task, apple) = StartTask(t, "production", "ringo", "りんご");
        var started = task.StartEventId;
        var r = await Attempt(t, "production", "ringo", "りんご", success: true, assessment: "minor_issue", eventIds: new[] { started }, fresh: true, note: "dropped the particle");
        Assert.True(r.IsSuccess, r.Detail);
        var d = DataOf(r);
        Assert.Equal("a1", d.GetProperty("attempt_id").GetString());
        Assert.Equal(t.Engine.SessionId, d.GetProperty("session_id").GetString());
        Assert.Equal(1, d.GetProperty("turn_id").GetInt64());
        Assert.Equal("production", d.GetProperty("target").GetProperty("dimension").GetString());
        Assert.Equal("ringo", d.GetProperty("target").GetProperty("key").GetString());
        Assert.Equal("りんご", d.GetProperty("transcript_excerpt").GetString());
        Assert.Equal("l1", d.GetProperty("source_utterance_id").GetString());
        Assert.Equal(new[] { started }, d.GetProperty("event_ids").EnumerateArray().Select(x => x.GetInt64()));
        Assert.Equal(new[] { "sensei" }, StrArray(d.GetProperty("actors")));
        Assert.Equal("task_active", d.GetProperty("engine_outcome").GetString());
        Assert.Equal(task.TaskId, d.GetProperty("task_id").GetString());
        Assert.Equal("minor_issue", d.GetProperty("language_assessment").GetString());
        Assert.True(d.GetProperty("communicative_success").GetBoolean());
        Assert.Equal("independent_success", d.GetProperty("outcome").GetString());
        Assert.True(d.GetProperty("is_fresh_variation").GetBoolean());
        Assert.Equal("low", d.GetProperty("uncertainty").GetString());
        Assert.Equal("model", d.GetProperty("assessment_source").GetString());
        Assert.Equal("rules-v1", d.GetProperty("rule_version").GetString());
        Assert.False(d.GetProperty("superseded").GetBoolean());
        Assert.Equal("dropped the particle", d.GetProperty("note").GetString());
        Assert.Equal(t.Clock.GetUtcNow(), d.GetProperty("at").GetDateTimeOffset());
        _ = apple;
    }

    [Fact]
    public async Task S46_the_stored_record_matches_what_the_tool_returned_and_has_no_affected_ids()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.Empty(r.AffectedIds);
        var stored = Profile(t).Evidence.Single();
        Assert.Equal(Str(r, "attempt_id"), stored.AttemptId);
        Assert.Equal("りんごをください", stored.TranscriptExcerpt);
        Assert.Equal(EvidenceRules.RuleVersion, stored.RuleVersion);
        Assert.Equal(EngineOutcomes.NoTask, stored.EngineOutcome);
        Assert.Null(stored.TaskId);
        Assert.False(stored.IsFreshVariation);
    }

    [Fact]
    public async Task S46_attempt_ids_count_per_profile_across_sessions()
    {
        var store = new InMemoryProfileStore();
        await using (var first = await TestEngine.CreateAsync(store: store))
        {
            await SayAndAttempt(first, "production", "ringo", "りんごをください");
            await SayAndAttempt(first, "production", "mizu", "みずをください");
        }
        await using var second = await TestEngine.CreateAsync(store: store);
        var r = await SayAndAttempt(second, "production", "ringo", "りんごをください");
        Assert.Equal("a3", Str(r, "attempt_id"));
        Assert.Equal(second.Engine.SessionId, Str(r, "session_id"));
    }

    [Fact]
    public async Task S46_engine_outcome_is_no_task_without_a_task()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.Equal("no_task", Str(r, "engine_outcome"));
    }

    // ---------------------------------------------------------------- S47 duplicates

    [Fact]
    public async Task S47_a_second_attempt_for_the_same_triple_in_the_same_turn_returns_the_first_with_duplicate_true()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var first = await Attempt(t, "production", "ringo", "りんご");
        Assert.False(DataOf(first).TryGetProperty("duplicate", out _));
        var saves = t.Store.SaveCount;
        var second = await Attempt(t, "production", "ringo", "りんごをください", success: false, assessment: "meaning_blocking_error");
        Assert.True(second.IsSuccess, second.Detail);
        Assert.True(DataOf(second).GetProperty("duplicate").GetBoolean());
        Assert.Equal(Str(first, "attempt_id"), Str(second, "attempt_id"));
        Assert.Equal("independent_success", Str(second, "outcome"));
        Assert.Equal("りんご", Str(second, "transcript_excerpt"));
        Assert.Single(Profile(t).Evidence);
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S47_another_key_or_another_dimension_in_the_same_turn_commits()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        Assert.True((await Attempt(t, "production", "ringo", "りんご")).IsSuccess);
        var otherKey = await Attempt(t, "production", "mizu", "ください");
        var otherDimension = await Attempt(t, "vocabulary", "ringo", "りんご");
        Assert.False(DataOf(otherKey).TryGetProperty("duplicate", out _));
        Assert.False(DataOf(otherDimension).TryGetProperty("duplicate", out _));
        Assert.Equal(3, Profile(t).Evidence.Count);
    }

    [Fact]
    public async Task S47_the_same_triple_in_the_next_turn_commits_again()
    {
        await using var t = await TestEngine.CreateAsync();
        await SayAndAttempt(t, "production", "ringo", "りんごをください");
        var again = await SayAndAttempt(t, "production", "ringo", "りんごをどうぞ");
        Assert.False(DataOf(again).TryGetProperty("duplicate", out _));
        Assert.Equal(2, Profile(t).Evidence.Count);
    }

    [Fact]
    public async Task S47_a_duplicate_whose_new_arguments_would_fail_is_still_the_first_attempts_data()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        await Attempt(t, "production", "ringo", "りんご");
        var second = await Attempt(t, "production", "ringo", "no such words");
        Assert.True(second.IsSuccess);
        Assert.True(DataOf(second).GetProperty("duplicate").GetBoolean());
    }

    // ---------------------------------------------------------------- S77 credentials

    [Theory]
    [InlineData("my key is sk-abcdefghijklmnop1234")]
    [InlineData("password: hunter22x")]
    [InlineData("token=abcdef123456")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789")]
    public async Task S77_an_excerpt_that_looks_like_a_credential_is_refused_and_nothing_is_stored(string said)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn(said);
        var saves = t.Store.SaveCount;
        var r = await Attempt(t, "production", "ringo", said);
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, r.ErrorCode);
        Assert.Empty(Profile(t).Evidence);
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S77_a_note_that_looks_like_a_credential_is_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "りんご", note: "api_key: zzzzzz-1234567");
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, r.ErrorCode);
        Assert.Empty(Profile(t).Evidence);
    }

    [Fact]
    public async Task S77_a_known_secret_in_the_excerpt_or_note_is_refused()
    {
        const string secret = "zq-example-secret-0007";
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { secret });
        t.Engine.BeginTurn("the word " + secret + " is odd");
        var inExcerpt = await Attempt(t, "production", "ringo", secret);
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, inExcerpt.ErrorCode);
        t.Engine.BeginTurn("the word is odd");
        var inNote = await Attempt(t, "production", "ringo", "word", note: "heard " + secret);
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, inNote.ErrorCode);
        Assert.DoesNotContain(secret, t.Store.RawJson("default"));
    }

    [Fact]
    public async Task S78_ordinary_words_like_password_do_not_trip_the_credential_check()
    {
        await using var t = await TestEngine.CreateAsync();
        const string said = "今日はpasswordという単語を練習したい";
        t.Engine.BeginTurn(said);
        var r = await Attempt(t, "vocabulary", "password", said, note: "API key is a good word to study");
        Assert.True(r.IsSuccess, r.Detail);
    }

    // ---------------------------------------------------------------- S96 binding

    [Fact]
    public async Task S96_the_attempt_binds_to_the_active_task()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (task, _) = StartTask(t, "production", "ringo");
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal(task.TaskId, Str(r, "task_id"));
        Assert.Equal("task_active", Str(r, "engine_outcome"));
    }

    [Fact]
    public async Task S96_an_explicit_task_id_must_name_an_active_or_recent_task()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (task, _) = StartTask(t, "production", "ringo");
        var ok = await Attempt(t, "production", "ringo", "りんご", taskId: task.TaskId);
        Assert.True(ok.IsSuccess, ok.Detail);
        t.Engine.BeginTurn("りんごをください");
        var unknown = await Attempt(t, "production", "ringo", "りんご", taskId: "t99");
        Assert.Equal(ToolErrorCodes.NoActiveTask, unknown.ErrorCode);
    }

    [Fact]
    public async Task S96_a_task_completed_in_this_or_the_previous_turn_is_bound_with_task_completed()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        var (task, apple) = StartTask(t, "production", "ringo");
        t.Engine.PlayerMove(apple, "left", null);
        Assert.Null(t.Engine.GetActiveTask());
        var sameTurn = await Attempt(t, "production", "ringo", "start");
        Assert.Equal(task.TaskId, Str(sameTurn, "task_id"));
        Assert.Equal("task_completed", Str(sameTurn, "engine_outcome"));

        t.Engine.BeginTurn("りんご");
        var nextTurn = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal(task.TaskId, Str(nextTurn, "task_id"));
        Assert.Equal("task_completed", Str(nextTurn, "engine_outcome"));
    }

    [Fact]
    public async Task S96_a_task_completed_two_turns_ago_is_not_bound()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        var (task, apple) = StartTask(t, "production", "ringo");
        t.Engine.PlayerMove(apple, "left", null);
        t.Engine.BeginTurn("one");
        t.Engine.BeginTurn("りんご");
        var implicitBind = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal("no_task", Str(implicitBind, "engine_outcome"));
        Assert.False(DataOf(implicitBind).TryGetProperty("task_id", out _));
        t.Engine.BeginTurn("りんご");
        var explicitBind = await Attempt(t, "production", "ringo", "りんご", taskId: task.TaskId);
        Assert.Equal(ToolErrorCodes.NoActiveTask, explicitBind.ErrorCode);
    }

    [Fact]
    public async Task S120_a_task_ended_by_end_activity_binds_with_task_ended()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        var (task, _) = StartTask(t, "production", "ringo");
        var end = await t.CallAsync("end_activity", """{"next_suggestion":"practice more"}""");
        Assert.True(end.IsSuccess, end.Detail);
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal(task.TaskId, Str(r, "task_id"));
        Assert.Equal("task_ended", Str(r, "engine_outcome"));
        var byId = await Attempt(t, "production", "mizu", "ください", taskId: task.TaskId);
        Assert.Equal("task_ended", Str(byId, "engine_outcome"));
    }

    [Fact]
    public async Task S121_an_abandoned_task_is_never_bound_implicitly()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        StartTask(t, "production", "ringo");
        lock (t.Core.Sync) t.Core.Tasks.CommitAbandon(t.Core.CurrentTurnId);
        var sameTurn = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal("no_task", Str(sameTurn, "engine_outcome"));
        Assert.False(DataOf(sameTurn).TryGetProperty("task_id", out _));
        var next = await SayAndAttempt(t, "production", "mizu", "みずをください");
        Assert.Equal("no_task", Str(next, "engine_outcome"));
    }

    [Fact]
    public async Task S121_an_explicit_task_id_binds_a_task_abandoned_in_this_or_the_previous_turn_with_task_ended()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (task, _) = StartTask(t, "production", "ringo");
        lock (t.Core.Sync) t.Core.Tasks.CommitAbandon(t.Core.CurrentTurnId);
        var sameTurn = await Attempt(t, "production", "ringo", "りんご", taskId: task.TaskId);
        Assert.True(sameTurn.IsSuccess, sameTurn.Detail);
        Assert.Equal(task.TaskId, Str(sameTurn, "task_id"));
        Assert.Equal("task_ended", Str(sameTurn, "engine_outcome"));

        t.Engine.BeginTurn("りんごをください");
        var nextTurn = await Attempt(t, "production", "ringo", "りんご", taskId: task.TaskId);
        Assert.Equal(task.TaskId, Str(nextTurn, "task_id"));
        Assert.Equal("task_ended", Str(nextTurn, "engine_outcome"));
    }

    [Fact]
    public async Task S121_an_explicit_task_id_abandoned_two_turns_ago_is_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (task, _) = StartTask(t, "production", "ringo");
        lock (t.Core.Sync) t.Core.Tasks.CommitAbandon(t.Core.CurrentTurnId);
        t.Engine.BeginTurn("one");
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "りんご", taskId: task.TaskId);
        Assert.Equal(ToolErrorCodes.NoActiveTask, r.ErrorCode);
    }

    [Fact]
    public async Task S121_implicit_binding_skips_a_more_recent_abandoned_task_for_the_latest_completed_one()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (first, apple) = StartTask(t, "production", "ringo");
        t.Engine.PlayerMove(apple, "left", null);
        StartTask(t, "production", "ringo");
        lock (t.Core.Sync) t.Core.Tasks.CommitAbandon(t.Core.CurrentTurnId);
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal(first.TaskId, Str(r, "task_id"));
        Assert.Equal("task_completed", Str(r, "engine_outcome"));
    }

    [Fact]
    public async Task S121_implicit_binding_prefers_the_active_task_over_a_finished_one()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (_, apple) = StartTask(t, "production", "ringo");
        t.Engine.PlayerMove(apple, "left", null);
        var (second, _) = StartTask(t, "production", "ringo");
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal(second.TaskId, Str(r, "task_id"));
        Assert.Equal("task_active", Str(r, "engine_outcome"));
    }

    [Fact]
    public async Task S121_implicit_binding_picks_the_most_recent_of_a_completed_and_an_ended_task()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (_, apple) = StartTask(t, "production", "ringo");
        t.Engine.PlayerMove(apple, "left", null);
        var (ended, _) = StartTask(t, "production", "ringo");
        lock (t.Core.Sync) t.Core.Tasks.CommitEnd(t.Core.CurrentTurnId);
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal(ended.TaskId, Str(r, "task_id"));
        Assert.Equal("task_ended", Str(r, "engine_outcome"));
    }

    [Fact]
    public async Task S95_a_listening_attempt_citing_a_sensei_event_after_the_task_started_is_invalid_arguments()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        var (_, apple) = StartTask(t, "listening", "ringo");
        var moved = SenseiMove(t, apple, "right", null).EventId;
        var r = await Attempt(t, "listening", "ringo", null, eventIds: new[] { moved });
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("listening_requires_player_events", r.Detail);
    }

    [Fact]
    public async Task S95_a_listening_attempt_may_cite_several_player_events_and_the_first_one_sets_the_source_position()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        var (_, apple) = StartTask(t, "listening", "ringo");
        t.Engine.PlayerMove(apple, "right", null);
        var first = MoveEventId(t);
        t.Engine.PlayerMove(apple, "left", null);
        var second = MoveEventId(t);
        var r = await Attempt(t, "listening", "ringo", null, eventIds: new[] { second, first });
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal(new[] { "player", "player" }, StrArray(DataOf(r).GetProperty("actors")));
    }

    // ---------------------------------------------------------------- S98 uncertainty

    [Fact]
    public async Task S98_uncertainty_defaults_to_low_and_high_is_stored()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var low = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal("low", Str(low, "uncertainty"));
        var high = await Attempt(t, "production", "mizu", "りんご", uncertainty: "high");
        Assert.Equal("high", Str(high, "uncertainty"));
        Assert.Equal("independent_success", Str(high, "outcome"));
    }

    [Fact]
    public async Task S98_an_unresolved_attempt_always_stores_high_uncertainty()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var r = await Attempt(t, "production", "ringo", "りんご", assessment: "unclear_input", uncertainty: "low");
        Assert.Equal("unresolved", Str(r, "outcome"));
        Assert.Equal("high", Str(r, "uncertainty"));
    }

    // ---------------------------------------------------------------- schema

    [Theory]
    [InlineData("""{"communicative_success":true,"language_assessment":"correct","transcript_excerpt":"a"}""")]
    [InlineData("""{"target":{"dimension":"grammar","key":"k"},"communicative_success":true,"language_assessment":"correct","transcript_excerpt":"a"}""")]
    [InlineData("""{"target":{"dimension":"production","key":""},"communicative_success":true,"language_assessment":"correct","transcript_excerpt":"a"}""")]
    [InlineData("""{"target":{"dimension":"production","key":"k"},"communicative_success":"yes","language_assessment":"correct","transcript_excerpt":"a"}""")]
    [InlineData("""{"target":{"dimension":"production","key":"k"},"communicative_success":true,"language_assessment":"fine","transcript_excerpt":"a"}""")]
    [InlineData("""{"target":{"dimension":"production","key":"k"},"communicative_success":true,"language_assessment":"correct","transcript_excerpt":"a","uncertainty":"medium"}""")]
    [InlineData("""{"target":{"dimension":"production","key":"k"},"communicative_success":true,"language_assessment":"correct","transcript_excerpt":"a","extra":1}""")]
    public async Task S21_record_attempt_arguments_are_strict(string json)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("a");
        var r = await t.CallAsync("record_attempt", json);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Empty(Profile(t).Evidence);
    }

    [Fact]
    public async Task S21_excerpt_and_note_length_limits()
    {
        await using var t = await TestEngine.CreateAsync();
        var long120 = new string('あ', 120);
        t.Engine.BeginTurn(long120);
        Assert.True((await Attempt(t, "production", "ringo", long120)).IsSuccess);
        t.Engine.BeginTurn(long120 + "あ");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await Attempt(t, "production", "mizu", long120 + "あ")).ErrorCode);
        t.Engine.BeginTurn(long120);
        Assert.True((await Attempt(t, "production", "ringo", long120, note: new string('n', 160))).IsSuccess);
        t.Engine.BeginTurn(long120);
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await Attempt(t, "production", "ringo", long120, note: new string('n', 161))).ErrorCode);
    }

    [Fact]
    public async Task S21_the_schema_keeps_the_excerpt_optional_for_listening_only_through_arguments_validation()
    {
        var schema = Sensei.Engine.Tools.ToolRegistry.CreateStandardTools().Single(x => x.Name == "record_attempt").JsonSchema;
        var required = schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.DoesNotContain("transcript_excerpt", required);
        await Task.CompletedTask;
    }
}

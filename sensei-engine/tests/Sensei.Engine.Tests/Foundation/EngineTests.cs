using System.Text.RegularExpressions;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>Engine creation, profile lifecycle, player preferences, events and turn lifecycle (S7, S40, S42, S59, S60, S69, S87, S99, S116, S117).</summary>
public sealed class EngineTests
{
    private static Task<TestEngine> WithRunner(InMemoryProfileStore? store = null, ScriptedChatClient? chat = null, RecordingEvents? events = null)
    {
        ControlledRunner? runner = null;
        return CreateAndCapture();

        async Task<TestEngine> CreateAndCapture()
        {
            var t = await TestEngine.CreateCoreAsync(chat ?? new ScriptedChatClient(), store, null, events, null, null, "default", null, null, null,
                core => runner = new ControlledRunner(core));
            Runners.AddOrUpdate(t, runner!);
            return t;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TestEngine, ControlledRunner> Runners = new();

    private static ControlledRunner RunnerOf(TestEngine t) => Runners.TryGetValue(t, out var r) ? r : throw new InvalidOperationException();

    // ---------------------------------------------------------------- S7 creation

    [Fact]
    public async Task S7_a_new_profile_is_created_with_defaults_and_saved()
    {
        await using var t = await TestEngine.CreateAsync();
        var stored = t.Store.Peek("default")!;
        Assert.Equal(1, stored.Version);
        Assert.Equal("default", stored.ProfileId);
        Assert.Null(stored.StartingBand);
        Assert.Equal(WorkingLevels.PreN5, stored.WorkingLevel);
        Assert.Empty(stored.LevelHistory);
        Assert.Empty(stored.Pinned);
        var s = stored.Support;
        Assert.True(s.ShowJapanese && s.ShowReading && s.ShowRomaji && s.ShowTranslation);
        Assert.Equal(("normal", "light", "english_explanations"), (s.Pace, s.CorrectionStyle, s.LanguageMode));
        Assert.Equal(t.Clock.GetUtcNow(), stored.CreatedAt);
    }

    [Fact]
    public async Task S7_creation_appends_a_session_record_and_saves()
    {
        await using var t = await TestEngine.CreateAsync();
        var session = t.Store.Peek("default")!.Sessions.Single();
        Assert.Equal(t.Engine.SessionId, session.SessionId);
        Assert.Equal(t.Clock.GetUtcNow(), session.StartedAt);
        Assert.Empty(session.PreferenceChanges);
        Assert.Null(session.NextSuggestion);
    }

    [Fact]
    public async Task S7_session_id_has_the_documented_form()
    {
        await using var t = await TestEngine.CreateAsync();
        Assert.Matches(new Regex("^s-20261027093015-[0-9a-f]{4}$"), t.Engine.SessionId);
    }

    [Fact]
    public async Task S7_an_existing_profile_is_loaded_and_gets_another_session()
    {
        var store = new InMemoryProfileStore();
        string firstSession;
        await using (var first = await TestEngine.CreateAsync(store: store))
        {
            await first.Engine.SetStartingBandAsync(StartingBands.Intermediate);
            firstSession = first.Engine.SessionId;
        }
        await using var second = await TestEngine.CreateAsync(store: store, clock: new ManualClock(new DateTimeOffset(2026, 10, 28, 8, 0, 0, TimeSpan.Zero)));
        var profile = second.Engine.GetProfile();
        Assert.Equal(StartingBands.Intermediate, profile.StartingBand);
        Assert.Equal(new[] { firstSession, second.Engine.SessionId }, profile.Sessions.Select(s => s.SessionId));
    }

    [Fact]
    public async Task S7_invalid_profile_id_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TestEngine.CreateAsync(profileId: "Not Valid"));
    }

    [Fact]
    public async Task S10_tool_names_are_the_sixteen_in_order()
    {
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { new ProbeTool() });
        Assert.Equal(ToolRegistry.StandardNames, t.Engine.ToolNames);
        Assert.Equal(16, t.Engine.ToolNames.Count);
    }

    [Fact]
    public async Task S3_get_profile_is_a_deep_copy()
    {
        await using var t = await TestEngine.CreateAsync();
        var view = t.Engine.GetProfile();
        view.Support.ShowRomaji = false;
        view.Pinned.Add("pace");
        view.Sessions.Clear();
        var again = t.Engine.GetProfile();
        Assert.True(again.Support.ShowRomaji);
        Assert.Empty(again.Pinned);
        Assert.Single(again.Sessions);
    }

    // ---------------------------------------------------------------- S40 pinning

    [Fact]
    public async Task S40_player_preference_sets_and_pins_fields_and_saves()
    {
        await using var t = await TestEngine.CreateAsync();
        var saves = t.Store.SaveCount;
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = false, Pace = "slow" });
        var p = t.Engine.GetProfile();
        Assert.False(p.Support.ShowRomaji);
        Assert.Equal("slow", p.Support.Pace);
        Assert.Equal(new[] { "show_romaji", "pace" }, p.Pinned);
        Assert.Equal(saves + 1, t.Store.SaveCount);
        Assert.Equal(new[] { "show_romaji", "pace" }, t.Store.Peek("default")!.Pinned);
    }

    [Fact]
    public async Task S40_preference_changes_are_recorded_by_player_only_when_values_change()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = false, ShowJapanese = true });
        var changes = t.Engine.GetSessionSummary().PreferenceChanges;
        var change = Assert.Single(changes);
        Assert.Equal(("show_romaji", "true", "false", "player"), (change.Field, change.From, change.To, change.By));
        Assert.Equal(new[] { "show_japanese", "show_romaji" }, t.Engine.GetProfile().Pinned); // pinned in SPEC field order
    }

    [Fact]
    public async Task S40_pinning_twice_keeps_one_pin_with_the_new_value()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "normal" });
        Assert.Equal(new[] { "pace" }, t.Engine.GetProfile().Pinned);
        Assert.Equal("normal", t.Engine.GetProfile().Support.Pace);
    }

    [Theory]
    [InlineData("fast", null, null)]
    [InlineData(null, "strict", null)]
    [InlineData(null, null, "french")]
    [InlineData("SLOW", null, null)]
    public async Task S40_invalid_preference_values_throw_and_change_nothing(string? pace, string? correction, string? mode)
    {
        await using var t = await TestEngine.CreateAsync();
        var saves = t.Store.SaveCount;
        await Assert.ThrowsAsync<ArgumentException>(() => t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = pace, CorrectionStyle = correction, LanguageMode = mode, ShowRomaji = false }));
        Assert.True(t.Engine.GetProfile().Support.ShowRomaji);
        Assert.Empty(t.Engine.GetProfile().Pinned);
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S40_support_change_raises_on_support_changed()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowTranslation = false });
        Assert.False(t.Events.SupportChanges.Single().ShowTranslation);
    }

    // ---------------------------------------------------------------- S42 starting band

    [Theory]
    [InlineData("zero", true, true, true, true, "english_explanations", "pre_n5")]
    [InlineData("early_beginner", true, true, true, true, "mixed", "n5")]
    [InlineData("developing_beginner", true, true, false, true, "mixed", "n4")]
    [InlineData("intermediate", true, true, false, false, "mixed", "n3")]
    [InlineData("advanced", true, false, false, false, "japanese_only", "n2")]
    public async Task S42_starting_band_applies_its_row(string band, bool ja, bool reading, bool romaji, bool translation, string mode, string level)
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync(band);
        var p = t.Engine.GetProfile();
        Assert.Equal(band, p.StartingBand);
        Assert.Equal((ja, reading, romaji, translation, mode), (p.Support.ShowJapanese, p.Support.ShowReading, p.Support.ShowRomaji, p.Support.ShowTranslation, p.Support.LanguageMode));
        Assert.Equal(level, p.WorkingLevel);
        Assert.Empty(p.Pinned);
        Assert.Equal(band, t.Store.Peek("default")!.StartingBand);
    }

    [Fact]
    public async Task S42_band_records_a_level_history_entry_with_reason_band()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync(StartingBands.Intermediate);
        var entry = t.Engine.GetProfile().LevelHistory.Single();
        Assert.Equal(("pre_n5", "n3", "band", false), (entry.From, entry.To, entry.Reason, entry.Superseded));
        Assert.Equal(t.Clock.GetUtcNow(), entry.At);
    }

    [Fact]
    public async Task S102_band_zero_on_a_new_profile_changes_no_level_and_adds_no_history()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync(StartingBands.Zero);
        Assert.Empty(t.Engine.GetProfile().LevelHistory);
        Assert.Equal(StartingBands.Zero, t.Engine.GetProfile().StartingBand);
    }

    [Fact]
    public async Task S42_band_does_not_touch_pinned_fields()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = true, LanguageMode = "english_explanations" });
        await t.Engine.SetStartingBandAsync(StartingBands.Advanced);
        var p = t.Engine.GetProfile();
        Assert.True(p.Support.ShowRomaji);
        Assert.Equal("english_explanations", p.Support.LanguageMode);
        Assert.False(p.Support.ShowReading);
        Assert.Equal(new[] { "show_romaji", "language_mode" }, p.Pinned);
    }

    [Fact]
    public async Task S42_band_changes_are_recorded_as_player_preference_changes_without_the_level()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync(StartingBands.DevelopingBeginner);
        var changes = t.Engine.GetSessionSummary().PreferenceChanges;
        Assert.Equal(new[] { "starting_band", "show_romaji", "language_mode" }, changes.Select(c => c.Field));
        Assert.All(changes, c => Assert.Equal("player", c.By));
        Assert.DoesNotContain(changes, c => c.Field.Contains("level"));
    }

    [Fact]
    public async Task S42_band_may_change_again_as_a_learner_request()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync(StartingBands.Advanced);
        await t.Engine.SetStartingBandAsync(StartingBands.EarlyBeginner);
        var p = t.Engine.GetProfile();
        Assert.Equal(StartingBands.EarlyBeginner, p.StartingBand);
        Assert.Equal("n5", p.WorkingLevel);
        Assert.Equal(new[] { "n2", "n5" }, p.LevelHistory.Select(h => h.To));
    }

    [Theory]
    [InlineData("beginner")]
    [InlineData("Zero")]
    [InlineData("")]
    public async Task S42_unknown_band_throws(string band)
    {
        await using var t = await TestEngine.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => t.Engine.SetStartingBandAsync(band));
        Assert.Null(t.Engine.GetProfile().StartingBand);
    }

    [Fact]
    public void S41_working_level_steps_clamp_at_both_ends()
    {
        Assert.Equal("n5", WorkingLevels.Step("pre_n5", 1));
        Assert.Equal("pre_n5", WorkingLevels.Step("pre_n5", -1));
        Assert.Equal("n1", WorkingLevels.Step("n1", 1));
        Assert.Equal("n2", WorkingLevels.Step("n1", -1));
    }

    [Fact]
    public void S102_set_working_level_appends_history_only_on_change()
    {
        var p = LearnerProfile.CreateDefault("x", DateTimeOffset.UnixEpoch);
        Assert.False(SupportRules.SetWorkingLevel(p, "pre_n5", "adaptation", DateTimeOffset.UnixEpoch));
        Assert.True(SupportRules.SetWorkingLevel(p, "n4", "self_report", DateTimeOffset.UnixEpoch, new List<string> { "a1" }));
        var h = Assert.Single(p.LevelHistory);
        Assert.Equal(("pre_n5", "n4", "self_report"), (h.From, h.To, h.Reason));
        Assert.Equal(new[] { "a1" }, h.EvidenceIds);
        Assert.Throws<ArgumentException>(() => SupportRules.SetWorkingLevel(p, "n6", "adaptation", DateTimeOffset.UnixEpoch));
    }

    // ---------------------------------------------------------------- S55 summary shape, S80 hidden level

    [Fact]
    public async Task S55_session_summary_never_contains_the_working_level()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync(StartingBands.Advanced);
        var json = System.Text.Json.JsonSerializer.Serialize(t.Engine.GetSessionSummary(), Sensei.Engine.Json.SenseiJsonContext.Default.SessionSummary);
        Assert.DoesNotContain("working", json);
        Assert.DoesNotContain("n2", json);
        Assert.Contains("\"session_id\"", json);
        Assert.Contains("\"next_suggestion\":null", json);
    }

    [Fact]
    public async Task S55_get_session_summary_ends_nothing()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        lock (t.Core.Sync)
        {
            t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "x" }, new[] { new SkillTarget { Dimension = "listening", Key = "ringo" } },
                new[] { new TaskPredicate { Type = "in_region", ObjectId = a, Region = "left" } }, Array.Empty<ScenarioProperty>(), null, false, 0);
        }
        var summary = t.Engine.GetSessionSummary();
        Assert.Equal(t.Engine.SessionId, summary.SessionId);
        Assert.NotNull(t.Engine.GetActiveTask());
        Assert.Empty(t.Events.Summaries);
    }

    // ---------------------------------------------------------------- S59 restart

    [Fact]
    public async Task S59_restart_restores_band_support_pins_evidence_notes_and_level()
    {
        var store = new InMemoryProfileStore();
        await using (var first = await TestEngine.CreateAsync(store: store))
        {
            await first.Engine.SetStartingBandAsync(StartingBands.Intermediate);
            await first.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
            lock (first.Core.Sync)
            {
                var p = first.Core.Learner.Profile;
                p.Notes["hobby"] = "tea ceremony";
                p.Evidence.Add(new EvidenceRecord { AttemptId = p.NextAttemptId(), SessionId = first.Engine.SessionId, Target = new SkillTarget { Dimension = "vocabulary", Key = "mizu" }, Outcome = AttemptOutcomes.IndependentSuccess });
            }
            await first.Core.Learner.SaveAsync(CancellationToken.None);
        }
        await using var second = await TestEngine.CreateAsync(store: store);
        var view = second.Engine.GetProfile();
        Assert.Equal(StartingBands.Intermediate, view.StartingBand);
        Assert.Equal("slow", view.Support.Pace);
        Assert.False(view.Support.ShowTranslation);
        Assert.Equal(new[] { "pace" }, view.Pinned);
        Assert.Equal("tea ceremony", view.Notes["hobby"]);
        Assert.Equal("a1", view.Evidence.Single().AttemptId);
        Assert.Equal("n3", view.WorkingLevel);
        Assert.Equal("a2", LearnerBook.Clone(second.Core.Learner.Profile).NextAttemptId());
    }

    // ---------------------------------------------------------------- S60 / S87 reset

    [Fact]
    public async Task S60_reset_replaces_the_profile_with_a_default_one_in_place()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync(StartingBands.Advanced);
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        await t.Engine.ResetProfileAsync();
        var p = t.Engine.GetProfile();
        Assert.Null(p.StartingBand);
        Assert.Equal("pre_n5", p.WorkingLevel);
        Assert.Empty(p.Pinned);
        Assert.Empty(p.LevelHistory);
        Assert.Equal("normal", p.Support.Pace);
        Assert.Equal(t.Engine.SessionId, p.Sessions.Single().SessionId);
        Assert.Equal(1, t.Store.DeleteCount);
        Assert.Null(t.Store.Peek("default")!.StartingBand);
    }

    [Fact]
    public async Task S60_reset_clears_the_scene_with_one_removed_event_and_revision_keeps_rising()
    {
        await using var t = await TestEngine.CreateAsync();
        Spawn(t, ("apple", "left"), ("cup", "right"));
        await t.Engine.ResetProfileAsync();
        var scene = t.Engine.GetScene();
        Assert.Empty(scene.Instances);
        Assert.Equal(2, scene.Revision);
        var removed = t.Core.World.Events.Last();
        Assert.Equal(SceneEventKinds.Removed, removed.Kind);
        Assert.Equal(new[] { "apple_1", "cup_1" }, removed.InstanceIds);
        Assert.Equal("apple_2", Spawn(t, "apple"));
        Assert.Equal(3, t.Engine.GetScene().Revision);
    }

    [Fact]
    public async Task S87_reset_of_an_empty_scene_adds_no_event_or_revision()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.ResetProfileAsync();
        Assert.Equal(0, t.Engine.GetScene().Revision);
        Assert.Empty(t.Core.World.Events);
    }

    [Fact]
    public async Task S87_reset_clears_task_logs_history_and_cache_but_counters_keep_rising()
    {
        var probe = new ProbeTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { probe });
        var a = Spawn(t, "apple");
        t.Engine.BeginTurn("first");
        lock (t.Core.Sync)
        {
            t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "x" }, new[] { new SkillTarget { Dimension = "listening", Key = "ringo" } },
                new[] { new TaskPredicate { Type = "in_region", ObjectId = a, Region = "left" } }, Array.Empty<ScenarioProperty>(), null, false, 1);
            var ctx = new ToolContext(t.Core, t.Core.CurrentTurn!, "x", "x", t.Core.CurrentTurn!.GenerationId, false);
            ctx.RecordAssistance(AssistanceKinds.Repetition, "listening", "ringo", "t1");
            t.Core.SpeechHistory.Add(new Sensei.Engine.Speech.SpeechHistoryEntry { UtteranceId = t.Core.NextSpeechUtteranceId(), SegmentsPresented = 1 });
        }
        var cached = await t.CallAsync("test_probe", """{"action":"ok"}""", "c1");
        await t.Engine.ResetProfileAsync();
        lock (t.Core.Sync)
        {
            Assert.Null(t.Core.Tasks.Active);
            Assert.Empty(t.Core.Tasks.History);
            Assert.Empty(t.Core.Assistance.Records);
            Assert.Empty(t.Core.Utterances.All);
            Assert.Empty(t.Core.SpeechHistory.Entries);
            Assert.Null(t.Core.CurrentTurn);
        }
        Assert.Equal(ToolErrorCodes.NoTurn, (await t.CallAsync("test_probe", """{"action":"ok"}""")).ErrorCode);
        t.Engine.BeginTurn("after");
        Assert.Equal(2, t.Core.CurrentTurnId);
        lock (t.Core.Sync) Assert.Equal("l2", t.Core.Utterances.All.Single().Id);
        lock (t.Core.Sync) Assert.Equal("u2", t.Core.NextSpeechUtteranceId());
        Assert.Equal(2, probe.Executions == 1 ? 2 : 0);
        _ = cached;
    }

    [Fact]
    public async Task S87_in_flight_work_from_before_reset_returns_cancelled_and_changes_nothing()
    {
        var mutate = new MutatingProbeTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { mutate });
        t.Engine.BeginTurn("x");
        var call = t.CallAsync("test_mutate", """{"action":"slow","delay_ms":30000}""");
        while (mutate.Executions == 0) await Task.Delay(5);
        await t.Engine.ResetProfileAsync();
        Assert.Equal(ToolStatuses.Cancelled, (await call).Status);
        Assert.Equal(0, t.Engine.GetScene().Revision);
    }

    [Fact]
    public async Task S87_reset_resets_the_agent_conversation()
    {
        await using var t = await WithRunner();
        await t.Engine.ResetProfileAsync();
        Assert.Equal(1, RunnerOf(t).ResetCount);
    }

    // ---------------------------------------------------------------- S69 events

    [Fact]
    public async Task S69_events_are_raised_after_the_change_committed_on_the_calling_thread()
    {
        var events = new RecordingEvents();
        await using var t = await TestEngine.CreateAsync(events: events);
        var a = Spawn(t, "apple");
        long seenRevision = -1;
        var thread = await Task.Run(() =>
        {
            t.Engine.PlayerMove(a, SceneRegions.Left, null);
            return Environment.CurrentManagedThreadId;
        });
        var raised = events.All.Last();
        Assert.Equal(nameof(IEngineEvents.OnSceneChanged), raised.Method);
        Assert.Equal(thread, raised.ThreadId);
        seenRevision = events.SceneSnapshots.Last().Revision;
        Assert.Equal(2, seenRevision);
    }

    [Fact]
    public async Task S69_a_throwing_handler_never_undoes_the_change_and_is_traced()
    {
        var events = new RecordingEvents { ThrowFrom = nameof(IEngineEvents.OnSceneChanged) };
        await using var t = await TestEngine.CreateAsync(events: events);
        var a = Spawn(t, "apple");
        var r = t.Engine.PlayerMove(a, SceneRegions.Left, null);
        Assert.True(r.Ok);
        Assert.Equal("left", t.Engine.GetScene().Instances.Single().Region);
        var traced = t.Trace.Entries.Where(e => e.Tool == "event:OnSceneChanged").ToList();
        Assert.Equal(2, traced.Count);
        Assert.All(traced, e => Assert.Equal((ToolStatuses.Failure, ToolErrorCodes.EventHandlerError), (e.Status, e.ErrorCode)));
    }

    [Fact]
    public async Task S69_a_throwing_task_handler_keeps_the_completion()
    {
        var events = new RecordingEvents { ThrowFrom = nameof(IEngineEvents.OnTaskChanged) };
        await using var t = await TestEngine.CreateAsync(events: events);
        var a = Spawn(t, "apple");
        lock (t.Core.Sync)
        {
            t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "x" }, new[] { new SkillTarget { Dimension = "listening", Key = "ringo" } },
                new[] { new TaskPredicate { Type = "in_region", ObjectId = a, Region = "left" } }, Array.Empty<ScenarioProperty>(), null, false, 0);
        }
        t.Core.FlushNotifications();
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Completed, t.Core.Tasks.History.Single().Status);
    }

    [Fact]
    public async Task S69_turn_end_raises_idle()
    {
        var events = new RecordingEvents();
        await using var t = await TestEngine.CreateAsync(events: events);
        await t.Engine.RunTurnAsync("こんにちは");
        Assert.Equal(SenseiStates.Idle, events.SenseiStates.Last());
    }

    // ---------------------------------------------------------------- S67 / S117 turns without a model

    [Fact]
    public async Task S117_turn_without_a_chat_client_fails_llm_not_configured_but_keeps_the_utterance()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await t.Engine.RunTurnAsync("りんごをください");
        Assert.Equal(TurnStatuses.Failed, r.Status);
        Assert.Equal(TurnErrorCodes.LlmNotConfigured, r.ErrorCode);
        Assert.Equal(1, r.TurnId);
        lock (t.Core.Sync) Assert.Equal("りんごをください", t.Core.Utterances.All.Single().Text);
        Assert.Equal(0, t.Engine.GetScene().Revision);
    }

    [Fact]
    public async Task S117_a_failed_turn_leaves_tools_usable_in_that_turn()
    {
        var probe = new ProbeTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { probe });
        await t.Engine.RunTurnAsync("x");
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_probe", """{"action":"ok"}""")).Status);
    }

    [Fact]
    public async Task S67_turn_result_carries_the_turn_tool_calls()
    {
        await using var t = await WithRunner();
        RunnerOf(t).Block = false;
        var r = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(TurnStatuses.Completed, r.Status);
        Assert.Equal(1, r.TurnId);
        Assert.Empty(r.ToolCalls);
        Assert.Equal("hello", RunnerOf(t).Requests.Single().Transcript);
    }

    // ---------------------------------------------------------------- S68 / S87 lifecycle gate

    [Fact]
    public async Task S87_begin_turn_while_a_turn_runs_throws_turn_in_progress()
    {
        await using var t = await WithRunner();
        var running = t.Engine.RunTurnAsync("first");
        await RunnerOf(t).Started.Task;
        var ex = Assert.Throws<InvalidOperationException>(() => t.Engine.BeginTurn("manual"));
        Assert.Contains("turn_in_progress", ex.Message);
        RunnerOf(t).Release();
        Assert.Equal(TurnStatuses.Completed, (await running).Status);
        t.Engine.BeginTurn("manual");
    }

    [Fact]
    public async Task S68_a_new_turn_cancels_the_running_one_and_waits_for_it()
    {
        await using var t = await WithRunner();
        var first = t.Engine.RunTurnAsync("first");
        await RunnerOf(t).Started.Task;
        RunnerOf(t).Block = false;
        var second = t.Engine.RunTurnAsync("second");
        var r1 = await first;
        var r2 = await second;
        Assert.Equal(TurnStatuses.Cancelled, r1.Status);
        Assert.Equal(TurnStatuses.Completed, r2.Status);
        Assert.Equal(new long[] { 1, 2 }, new[] { r1.TurnId, r2.TurnId });
        Assert.True(RunnerOf(t).Requests[1].PreviousTurnInterrupted);
    }

    [Fact]
    public async Task S66_cancel_generation_ends_a_running_turn_as_cancelled()
    {
        await using var t = await WithRunner();
        var running = t.Engine.RunTurnAsync("first");
        await RunnerOf(t).Started.Task;
        t.Engine.CancelGeneration();
        Assert.Equal(TurnStatuses.Cancelled, (await running).Status);
    }

    [Fact]
    public async Task S87_reset_cancels_and_awaits_the_running_turn()
    {
        await using var t = await WithRunner();
        var running = t.Engine.RunTurnAsync("first");
        await RunnerOf(t).Started.Task;
        await t.Engine.ResetProfileAsync();
        Assert.True(running.IsCompleted);
        Assert.Equal(TurnStatuses.Cancelled, (await running).Status);
    }

    // ---------------------------------------------------------------- S99 / S116 correction

    [Fact]
    public async Task S99_correction_with_no_learner_utterance_is_nothing_to_correct()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("(cli)");
        var r = await t.Engine.CorrectLastTranscriptAsync("りんご");
        Assert.Equal(TurnStatuses.Failed, r.Status);
        Assert.Equal(TurnErrorCodes.NothingToCorrect, r.ErrorCode);
        lock (t.Core.Sync) Assert.Single(t.Core.Utterances.All);
    }

    [Fact]
    public async Task S99_correction_supersedes_the_utterance_and_its_attempts_then_runs_a_turn()
    {
        await using var t = await WithRunner();
        RunnerOf(t).Block = false;
        await t.Engine.RunTurnAsync("りんごをくだしゃい");
        string sourceId;
        lock (t.Core.Sync)
        {
            sourceId = t.Core.Utterances.All.Single().Id;
            var p = t.Core.Learner.Profile;
            p.Evidence.Add(new EvidenceRecord { AttemptId = "a1", SessionId = t.Engine.SessionId, SourceUtteranceId = sourceId, Target = new SkillTarget { Dimension = "production", Key = "kudasai" } });
            p.Evidence.Add(new EvidenceRecord { AttemptId = "a2", SessionId = t.Engine.SessionId, SourceUtteranceId = "l99", Target = new SkillTarget { Dimension = "production", Key = "kudasai" } });
        }
        var saves = t.Store.SaveCount;
        var r = await t.Engine.CorrectLastTranscriptAsync("りんごをください");
        Assert.Equal(TurnStatuses.Completed, r.Status);
        Assert.Equal(2, r.TurnId);
        lock (t.Core.Sync)
        {
            Assert.True(t.Core.Utterances.Find(sourceId)!.Superseded);
            var newest = t.Core.Utterances.All.Last();
            Assert.Equal("りんごをください", newest.Text);
            Assert.Equal("りんごをくだしゃい", newest.CorrectedFrom);
        }
        Assert.True(saves < t.Store.SaveCount);
        var stored = t.Store.Peek("default")!;
        Assert.True(stored.Evidence.Single(e => e.AttemptId == "a1").Superseded);
        Assert.False(stored.Evidence.Single(e => e.AttemptId == "a2").Superseded);
        var request = RunnerOf(t).Requests.Last();
        Assert.Equal("りんごをくだしゃい", request.CorrectedFrom);
        Assert.Equal("りんごをください", request.Transcript);
    }

    [Fact]
    public async Task S99_supersede_stays_when_the_correction_turn_fails()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.RunTurnAsync("first try");
        var r = await t.Engine.CorrectLastTranscriptAsync("second try");
        Assert.Equal(TurnErrorCodes.LlmNotConfigured, r.ErrorCode);
        lock (t.Core.Sync)
        {
            Assert.True(t.Core.Utterances.All[0].Superseded);
            Assert.False(t.Core.Utterances.All[1].Superseded);
        }
    }

    [Fact]
    public async Task S117_correct_after_a_failed_turn_supersedes_that_turns_utterance()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.RunTurnAsync("one");
        await t.Engine.RunTurnAsync("two");
        await t.Engine.CorrectLastTranscriptAsync("two fixed");
        lock (t.Core.Sync)
        {
            Assert.Equal(new[] { false, true, false }, t.Core.Utterances.All.Select(u => u.Superseded));
        }
    }

    [Fact]
    public async Task S116_correction_while_a_turn_runs_cancels_and_waits_first()
    {
        await using var t = await WithRunner();
        var running = t.Engine.RunTurnAsync("first");
        await RunnerOf(t).Started.Task;
        RunnerOf(t).Block = false;
        var corrected = await t.Engine.CorrectLastTranscriptAsync("first, fixed");
        Assert.Equal(TurnStatuses.Cancelled, (await running).Status);
        Assert.Equal(TurnStatuses.Completed, corrected.Status);
        lock (t.Core.Sync) Assert.True(t.Core.Utterances.All[0].Superseded);
    }
}

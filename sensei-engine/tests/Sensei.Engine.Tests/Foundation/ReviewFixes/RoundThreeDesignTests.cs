using System.Collections;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Storage;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tests.World;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.Foundation.RoundThree;

/// <summary>
/// Review round 3, the orchestrator's design (D1 to D5) as a whole: the cases the reviewer's five files do not reach.
/// </summary>
public sealed class RoundThreeDesignTests
{
    private const string Secret = "fictional-round-three-credential";

    private sealed class UnreadableException : Exception
    {
        public override string Message => throw new IOException("the Message getter failed: " + Secret);
    }

    private sealed class DerivedIo(string message) : IOException(message);

    // ------------------------------------------------------------------ D1: host-facing exceptions never carry foreign text

    [Fact]
    public void D1_the_categories_are_the_fixed_set()
    {
        Assert.Equal(
            new[] { "event_handler_error", "internal_error", "invalid_configuration", "llm_error", "profile_store_error", "speech_output_error" },
            FaultCategories.All.OrderBy(c => c, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("CreateAsync", "create")]
    [InlineData("SetStartingBandAsync", "set_starting_band")]
    [InlineData("SetPreferenceByPlayerAsync", "set_preference_by_player")]
    [InlineData("CancelGeneration", "cancel_generation")]
    [InlineData("DisposeAsync", "dispose")]
    [InlineData("RunTurnAsync", "run_turn")]
    public void D1_the_operation_reads_as_the_public_member_in_snake_case(string member, string expected)
    {
        Assert.Equal(expected, ExceptionSanitizer.OperationName(member));
    }

    [Fact]
    public async Task D1_a_store_failure_says_the_operation_and_the_category_only()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Store.FailSavesWith = new IOException("store down " + Secret, new InvalidOperationException("inner " + Secret));

        var error = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));

        var safe = Assert.IsType<SenseiEngineException>(error);
        Assert.Equal("set_starting_band failed: profile_store_error", safe.Message);
        Assert.Null(safe.InnerException);
        Assert.Empty(safe.Data);
        Assert.Equal(typeof(IOException).FullName, safe.OriginalType);
    }

    [Fact]
    public async Task D1_a_failing_tool_body_is_internal_error_and_a_failing_runner_is_llm_error()
    {
        var tool = new ThrowingTool();
        await using var t = await TestEngine.CreateCoreAsync(new ScriptedChatClient(), null, null, null, null, null, "default", new[] { Secret }, new SenseiTool[] { tool }, null,
            _ => new ThrowingRunner());
        t.Engine.BeginTurn("x");

        var toolError = await Record.ExceptionAsync(() => t.CallAsync(tool.Name, """{"action":"ok"}"""));
        var runnerError = await Record.ExceptionAsync(() => t.Engine.RunTurnAsync("hello"));

        Assert.Equal("invoke_tool failed: internal_error", Assert.IsType<SenseiEngineException>(toolError).Message);
        Assert.Equal("run_turn failed: llm_error", Assert.IsType<SenseiEngineException>(runnerError).Message);
    }

    private sealed class ThrowingTool : ProbeTool
    {
        public ThrowingTool() : base("test_throw_r3")
        {
        }

        public override Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct) =>
            throw new IOException("tool failed " + Secret);
    }

    private sealed class ThrowingRunner : IAgentRunner
    {
        public Task<TurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct) => throw new HttpRequestException("provider " + Secret);

        public void ResetConversation()
        {
        }
    }

    [Fact]
    public async Task D1_a_dependency_exception_of_an_intended_type_is_recreated_with_an_engine_authored_message()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        using var cts = new CancellationTokenSource();

        t.Store.FailSavesWith = new InvalidOperationException("store state " + Secret);
        var invalid = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));
        t.Store.FailSavesWith = new ArgumentException("starting band must be one of fake " + Secret, "band");
        var argument = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));
        t.Store.FailSavesWith = new OperationCanceledException("store " + Secret, cts.Token);
        var cancelled = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));

        Assert.IsType<InvalidOperationException>(invalid);
        Assert.Equal("set_starting_band failed: profile_store_error", invalid!.Message);
        var bad = Assert.IsType<ArgumentException>(argument);
        Assert.Equal("set_starting_band failed: profile_store_error", bad.Message);
        Assert.Null(bad.ParamName);
        var canceled = Assert.IsType<OperationCanceledException>(cancelled);
        Assert.Equal("set_starting_band was canceled", canceled.Message);
        Assert.Equal(cts.Token, canceled.CancellationToken);
        foreach (var error in new[] { invalid, argument, cancelled })
        {
            Assert.Null(error!.InnerException);
            Assert.DoesNotContain(Secret, error.ToString());
        }
    }

    [Fact]
    public async Task D1_the_engines_own_intended_exceptions_keep_their_authored_message_and_parameter()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });

        var band = Assert.IsType<ArgumentException>(await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("not-a-band")));
        var patch = Assert.IsType<ArgumentNullException>(await Record.ExceptionAsync(() => t.Engine.SetPreferenceByPlayerAsync(null!)));

        Assert.Equal("band", band.ParamName);
        Assert.StartsWith("starting band must be one of", band.Message);
        Assert.Equal("patch", patch.ParamName);
    }

    [Fact]
    public async Task D1_a_foreign_exception_cannot_pass_for_an_engine_authored_one_by_copying_its_text()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Store.FailSavesWith = new InvalidOperationException("turn_in_progress: a turn is running");

        var error = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));

        Assert.IsType<InvalidOperationException>(error);
        Assert.DoesNotContain("turn_in_progress", error!.Message);
    }

    [Fact]
    public async Task D1_a_corrupt_profile_is_recreated_from_the_requested_id_and_nothing_the_store_said()
    {
        var trace = new RecordingTraceSink();
        var store = new CorruptOnLoadStore(new ProfileCorruptException("other-" + Secret, "/data/" + Secret + ".json", new FormatException("bad " + Secret)));
        var options = new SenseiEngineOptions
        {
            ChatClient = null,
            Speech = new RecordingSpeechOutput(),
            ProfileStore = store,
            Trace = trace,
            KnownSecrets = new[] { Secret },
        };

        var error = await Record.ExceptionAsync(() => SenseiEngine.CreateAsync(options));

        var corrupt = Assert.IsType<ProfileCorruptException>(error);
        Assert.Equal("default", corrupt.ProfileId);
        Assert.Equal("default.json", corrupt.FilePath);
        // Round-four ruling (F02): the message is engine-authored and holds no caller-supplied text, so no profile id either.
        Assert.Equal(ProfileCorruptException.FixedMessage, corrupt.Message);
        Assert.Null(corrupt.InnerException);
        Assert.DoesNotContain(Secret, corrupt.ToString());
        var entry = Assert.Single(trace.Entries, e => e.Tool == "engine:CreateAsync");
        Assert.DoesNotContain(Secret, entry.Arguments);
        Assert.Contains("bad [redacted]", entry.Arguments);
    }

    private sealed class CorruptOnLoadStore(Exception failure) : IProfileStore
    {
        public Task<LearnerProfile?> LoadAsync(string profileId, CancellationToken ct) => throw failure;
        public Task SaveAsync(LearnerProfile profile, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(string profileId, CancellationToken ct) => Task.CompletedTask;
    }

    // ------------------------------------------------------------------ D2: the trace description

    [Fact]
    public void D2_a_message_is_read_only_for_the_exact_listed_types()
    {
        var secrets = new Secrets(new[] { Secret });
        var message = "m-" + Secret;
        var listed = new Exception[]
        {
            new IOException(message), new FileNotFoundException(message), new DirectoryNotFoundException(message),
            new UnauthorizedAccessException(message), new TimeoutException(message), new HttpRequestException(message),
            new JsonException(message), new ArgumentException(message), new InvalidOperationException(message),
            new NotSupportedException(message), new FormatException(message),
        };
        var unlisted = new Exception[]
        {
            new DerivedIo(message), new AggregateException(message), new KeyNotFoundException(message), new InvalidDataException(message),
            new ArgumentNullException("p", message), new OperationCanceledException(message), new UnreadableException(),
        };

        foreach (var ex in listed)
        {
            var text = ExceptionSanitizer.Describe(ex, secrets);
            Assert.Contains("m-[redacted]", text);
            Assert.DoesNotContain(Secret, text);
        }
        foreach (var ex in unlisted)
        {
            var text = ExceptionSanitizer.Describe(ex, secrets);
            Assert.DoesNotContain("m-", text);
            Assert.DoesNotContain("Message getter", text);
            Assert.Equal("[0] " + ex.GetType().FullName + "\n", text);
        }
    }

    [Fact]
    public void D2_an_aggregate_with_unreadable_children_is_walked_through_its_inner_exceptions()
    {
        var secrets = new Secrets(new[] { Secret });
        var aggregate = new AggregateException("outer " + Secret, new UnreadableException(), new IOException("second " + Secret));

        var text = ExceptionSanitizer.Describe(aggregate, secrets);

        Assert.DoesNotContain(Secret, text);
        Assert.Contains("[1] inside [0] " + typeof(UnreadableException).FullName, text);
        Assert.Contains("[2] inside [0] System.IO.IOException: second [redacted]", text);
    }

    [Fact]
    public void D2_a_cycle_is_described_once_without_recursion()
    {
        var secrets = new Secrets(new[] { Secret });
        var aggregate = new AggregateException("outer", new IOException("leaf " + Secret));
        var field = typeof(AggregateException).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(Exception[]));
        ((Exception[])field.GetValue(aggregate)!)[0] = aggregate;

        var text = ExceptionSanitizer.Describe(aggregate, secrets);

        Assert.Contains("refers back to", text);
        Assert.DoesNotContain(Secret, text);
    }

    [Fact]
    public void D2_a_wide_aggregate_is_cut_at_the_node_limit()
    {
        var secrets = new Secrets(new[] { Secret });
        var children = Enumerable.Range(0, 5000).Select(i => (Exception)new IOException("child " + i)).ToArray();

        var text = ExceptionSanitizer.Describe(new AggregateException(children), secrets);

        Assert.Equal(ExceptionSanitizer.MaxNodes, text.Split('\n').Count(line => line.StartsWith('[')));
        Assert.Contains("(more exceptions not shown)", text);
    }

    [Fact]
    public void D2_the_unreadable_marker_is_the_bracketed_word()
    {
        Assert.Equal("[unavailable]", ExceptionSanitizer.Unavailable);
        Assert.Equal(32, ExceptionSanitizer.MaxNodes);
    }

    // ------------------------------------------------------------------ D3: secrets setup is inside the CreateAsync boundary

    private sealed class FailingSecrets(bool yieldFirst) : IReadOnlyList<string>
    {
        public int Count => 1;
        public string this[int index] => Secret;

        public IEnumerator<string> GetEnumerator()
        {
            if (yieldFirst) yield return Secret;
            throw new UnreadableException();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task D3_a_failing_known_secrets_list_is_an_invalid_configuration_with_no_foreign_text_and_no_trace(bool yieldFirst)
    {
        var trace = new RecordingTraceSink();
        var options = new SenseiEngineOptions
        {
            ChatClient = null,
            Speech = new RecordingSpeechOutput(),
            ProfileStore = new InMemoryProfileStore(),
            Trace = trace,
            KnownSecrets = new FailingSecrets(yieldFirst),
        };

        var error = await Record.ExceptionAsync(() => SenseiEngine.CreateAsync(options));

        var safe = Assert.IsType<SenseiEngineException>(error);
        Assert.Equal("create failed: invalid_configuration", safe.Message);
        Assert.Null(safe.InnerException);
        Assert.DoesNotContain(Secret, safe.ToString());
        Assert.Empty(trace.Entries);
    }

    // ------------------------------------------------------------------ D4: committed is about changes

    private sealed class CommitThen(string name, bool fault, bool waitForCancellation) : ProbeTool(name)
    {
        public TaskCompletionSource<bool> Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool IsMutating => true;

        public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
        {
            var result = context.Locked(() =>
            {
                var e = context.World.CommitSpawn(new[] { new SpawnRequest("apple", SceneRegions.Center) }, SceneActors.Sensei, context.TurnId);
                return context.Success(e.InstanceIds);
            });
            Committed.TrySetResult(true);
            if (waitForCancellation) await Task.Delay(Timeout.Infinite, ct);
            await Release.Task;
            if (fault) throw new IOException("late fault " + Secret);
            return result;
        }
    }

    public enum Transition
    {
        CancelGeneration,
        NextTurn,
    }

    [Theory]
    [InlineData(Transition.CancelGeneration, false, false)]
    [InlineData(Transition.NextTurn, false, false)]
    [InlineData(Transition.CancelGeneration, true, false)]
    [InlineData(Transition.NextTurn, true, false)]
    [InlineData(Transition.CancelGeneration, false, true)]
    [InlineData(Transition.NextTurn, false, true)]
    public async Task D4_a_scene_commit_is_success_and_notifies_however_its_call_ends_after_an_ordinary_generation_transition(Transition transition, bool fault, bool cancellation)
    {
        var tool = new CommitThen("test_commit_then", fault, cancellation);
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool }, knownSecrets: new[] { Secret });
        t.Engine.BeginTurn("first");
        var pending = t.CallAsync(tool.Name, """{"action":"ok"}""", "held");
        await tool.Committed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = t.CallAsync(tool.Name, """{"action":"ok"}""", "held");
        if (transition == Transition.NextTurn) t.Engine.BeginTurn("second");
        else t.Engine.CancelGeneration();
        tool.Release.TrySetResult(true);

        var first = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await duplicate.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ToolStatuses.Success, first.Status);
        Assert.Equal(first.Status, second.Status);
        Assert.Single(t.Engine.GetScene().Instances);
        Assert.Single(t.Events.SceneEvents, e => e.Kind == SceneEventKinds.Spawned);
    }

    [Fact]
    public async Task D4_a_scene_commit_removed_by_a_reset_is_cancelled_and_never_notified_again()
    {
        var tool = new CommitThen("test_commit_reset", fault: true, waitForCancellation: false);
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("first");
        var pending = t.CallAsync(tool.Name, """{"action":"ok"}""", "held");
        await tool.Committed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await t.Engine.ResetProfileAsync();
        var afterReset = t.Events.SceneEvents.Count;
        tool.Release.TrySetResult(true);

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ToolStatuses.Cancelled, result.Status);
        Assert.Empty(t.Engine.GetScene().Instances);
        Assert.Equal(afterReset, t.Events.SceneEvents.Count);
    }

    [Fact]
    public async Task D4_the_reset_still_notifies_its_own_task_abandonment_and_scene_removal()
    {
        await using var t = await WorldKit.StartAsync(("apple", "left"));
        await WorldKit.StartTaskAsync(t, WorldKit.Predicates(WorldKit.Obj("apple_1", "right")));
        Assert.NotNull(t.Engine.GetActiveTask());
        t.Events.Clear();

        await t.Engine.ResetProfileAsync();

        Assert.Contains(t.Events.SceneEvents, e => e.Kind == SceneEventKinds.Removed);
        Assert.Contains(t.Events.Tasks, x => x.Status == TaskStatuses.Abandoned);
        Assert.Single(t.Events.SupportChanges);
    }

    [Fact]
    public async Task D4_a_profile_save_that_fails_after_the_commit_keeps_success_is_traced_and_retried_by_the_next_write()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Engine.BeginTurn("first");
        t.Store.FailSavesWith = new IOException("disk full " + Secret);

        var result = await t.CallAsync("remember_note", """{"key":"interest","value":"music"}""", "note");

        Assert.Equal(ToolStatuses.Success, result.Status);
        Assert.True(t.Engine.GetProfile().Notes.ContainsKey("interest"));
        Assert.False(t.Store.Peek("default")!.Notes.ContainsKey("interest"));
        var entry = Assert.Single(t.Trace.Entries, e => e.ErrorCode == ToolErrorCodes.ProfileSaveFailed);
        Assert.Equal("engine:profile_save", entry.Tool);
        Assert.Equal("note", entry.CallId);
        Assert.Equal(ToolStatuses.Failure, entry.Status);
        Assert.Contains("[0] System.IO.IOException: disk full [redacted]", entry.Arguments);
        Assert.DoesNotContain(Secret, entry.Arguments);

        t.Store.FailSavesWith = null;
        await t.Engine.SetStartingBandAsync("advanced");

        Assert.True(t.Store.Peek("default")!.Notes.ContainsKey("interest"));
    }

    [Fact]
    public async Task D4_a_profile_save_that_failed_is_retried_on_dispose()
    {
        var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("first");
        t.Store.FailSavesWith = new IOException("disk full");
        Assert.True((await t.CallAsync("remember_note", """{"key":"interest","value":"music"}""")).IsSuccess);
        t.Store.FailSavesWith = null;

        await t.DisposeAsync();

        Assert.True(t.Store.Peek("default")!.Notes.ContainsKey("interest"));
    }

    [Fact]
    public async Task D4_a_retry_on_dispose_that_fails_again_is_traced_and_does_not_throw()
    {
        var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Engine.BeginTurn("first");
        t.Store.FailSavesWith = new IOException("disk full " + Secret);
        Assert.True((await t.CallAsync("remember_note", """{"key":"interest","value":"music"}""")).IsSuccess);

        var error = await Record.ExceptionAsync(async () => await t.DisposeAsync());

        Assert.Null(error);
        Assert.Equal(2, t.Trace.Entries.Count(e => e.ErrorCode == ToolErrorCodes.ProfileSaveFailed));
        Assert.DoesNotContain(t.Trace.Entries, e => e.Arguments.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task D4_a_player_preference_whose_save_fails_still_throws_and_is_retried_on_dispose()
    {
        var t = await TestEngine.CreateAsync();
        t.Store.FailSavesWith = new IOException("disk full");

        var error = await Record.ExceptionAsync(() => t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" }));
        t.Store.FailSavesWith = null;
        await t.DisposeAsync();

        Assert.IsType<SenseiEngineException>(error);
        Assert.Equal("slow", t.Engine.GetProfile().Support.Pace);
        Assert.Equal("slow", t.Store.Peek("default")!.Support.Pace);
    }

    [Fact]
    public void D4_the_read_only_built_in_tools_and_the_speech_gates_use_the_read_path()
    {
        var root = LibraryDirectory();
        foreach (var file in new[] { "Tools/World/InspectSceneTool.cs", "Tools/Learner/QueryLearnerTool.cs", "Tools/Speech/SpeakTool.cs", "Tools/Speech/ShowHintTool.cs", "Tools/Speech/ReplaySpeechTool.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, file));
            Assert.Contains("context.Read(", source);
            Assert.DoesNotContain("context.Locked(", source);
        }
        foreach (var file in new[] { "Tools/Learner/RememberNoteTool.cs", "Tools/Learner/SetSupportTool.cs", "Tools/Learner/RecordAttemptTool.cs", "Tools/Learner/EndActivityTool.cs" })
            Assert.Contains("context.Locked(", File.ReadAllText(Path.Combine(root, file)));
    }

    // ------------------------------------------------------------------ D5: handler and speech failure isolation

    private sealed class FlakyClock : TimeProvider
    {
        public bool Broken { get; set; }

        public override DateTimeOffset GetUtcNow() => Broken ? throw new InvalidOperationException("clock failed") : new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class BreakingEvents(FlakyClock clock) : IEngineEvents
    {
        public void OnSceneChanged(SceneEvent e, SceneSnapshot scene)
        {
            clock.Broken = true;
            throw new UnreadableException();
        }

        public void OnHighlight(IReadOnlyList<string> ids, int durationMs) { }
        public void OnAnimate(string animation, string? targetId) { }
        public void OnHint(HintCard card) { }
        public void OnSupportChanged(SupportSettings settings) { }
        public void OnTaskChanged(TaskView task) { }
        public void OnSummary(SessionSummary summary) { }
        public void OnSenseiState(string state) { }
    }

    [Fact]
    public async Task D5_reporting_a_handler_failure_cannot_itself_fail_the_committed_call()
    {
        var clock = new FlakyClock();
        await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
        {
            ChatClient = null,
            Speech = new RecordingSpeechOutput(),
            ProfileStore = new InMemoryProfileStore(),
            Events = new BreakingEvents(clock),
            Clock = clock,
        });
        engine.BeginTurn("hello");

        var result = await engine.InvokeToolAsync("spawn_objects", """{"objects":[{"catalog_id":"apple","region":"center"}]}""", "spawn");

        Assert.Equal(ToolStatuses.Success, result.Status);
        Assert.Single(engine.GetScene().Instances);
        clock.Broken = false;
    }

    [Fact]
    public async Task D5_a_handler_failure_is_traced_with_its_type_and_never_an_unlisted_message()
    {
        var trace = new RecordingTraceSink();
        var events = new RecordingEvents { ThrowFrom = nameof(IEngineEvents.OnSceneChanged) };
        await using var t = await TestEngine.CreateAsync(events: events, trace: trace, knownSecrets: new[] { Secret });
        t.Engine.BeginTurn("hello");

        var result = await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"apple","region":"center"}]}""");

        Assert.Equal(ToolStatuses.Success, result.Status);
        var entry = Assert.Single(trace.Entries, e => e.ErrorCode == ToolErrorCodes.EventHandlerError);
        Assert.Equal("event:OnSceneChanged", entry.Tool);
        Assert.Equal("System.InvalidOperationException: handler failure in OnSceneChanged", entry.Arguments);
    }

    [Fact]
    public async Task D5_a_speech_port_exception_becomes_speech_failed_with_its_type_when_its_message_is_not_readable()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Engine.BeginTurn("hello");
        t.Speech.Outcome = _ => throw new UnreadableException();

        var result = await t.CallAsync("speak", """{"segments":[{"lang":"en","text":"hello"}]}""");

        Assert.Equal(ToolErrorCodes.SpeechFailed, result.ErrorCode);
        Assert.Contains(typeof(UnreadableException).FullName!, result.Detail);
        Assert.DoesNotContain(Secret, result.Detail);
    }

    [Fact]
    public async Task D5_a_speech_port_exception_of_a_listed_type_keeps_its_sanitized_message()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Engine.BeginTurn("hello");
        t.Speech.Outcome = _ => throw new IOException("audio device lost " + Secret);

        var result = await t.CallAsync("speak", """{"segments":[{"lang":"en","text":"hello"}]}""");

        Assert.Equal(ToolErrorCodes.SpeechFailed, result.ErrorCode);
        Assert.Contains("audio device lost [redacted]", result.Detail);
        Assert.DoesNotContain(Secret, result.Detail);
    }

    private static string LibraryDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var viaRoot = Path.Combine(dir.FullName, "sensei-engine", "src", "Sensei.Engine");
            if (File.Exists(Path.Combine(viaRoot, "Sensei.Engine.csproj"))) return viaRoot;
        }
        throw new DirectoryNotFoundException("sensei-engine/src/Sensei.Engine not found above " + AppContext.BaseDirectory);
    }
}

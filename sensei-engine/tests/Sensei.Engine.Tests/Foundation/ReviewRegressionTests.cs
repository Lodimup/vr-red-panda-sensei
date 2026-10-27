using System.Text;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Storage;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>Regressions for defects found by the adversarial review of the foundation slice.</summary>
public sealed class ReviewRegressionTests
{
    // S68: queued turns

    [Fact]
    public async Task S68_an_older_queued_turn_never_runs_after_a_newer_one()
    {
        ControlledRunner? runner = null;
        await using var t = await TestEngine.CreateCoreAsync(new ScriptedChatClient(), null, null, null, null, null, "default", null, null, null, c => runner = new ControlledRunner(c));
        var a = t.Engine.RunTurnAsync("a");
        await runner!.Started.Task;
        runner.Block = false;
        var b = t.Engine.RunTurnAsync("b");
        var c = t.Engine.RunTurnAsync("c");
        var results = await Task.WhenAll(a, b, c);
        Assert.Equal(new[] { "a", "c" }, runner.Requests.Select(r => r.Transcript));
        Assert.Equal(new[] { TurnStatuses.Cancelled, TurnStatuses.Cancelled, TurnStatuses.Completed }, results.Select(r => r.Status));
        lock (t.Core.Sync) Assert.Equal(new[] { "a", "c" }, t.Core.Utterances.All.Select(u => u.Text));
    }

    [Fact]
    public async Task S87_reset_supersedes_queued_turns()
    {
        ControlledRunner? runner = null;
        await using var t = await TestEngine.CreateCoreAsync(new ScriptedChatClient(), null, null, null, null, null, "default", null, null, null, c => runner = new ControlledRunner(c));
        var a = t.Engine.RunTurnAsync("a");
        await runner!.Started.Task;
        var b = t.Engine.RunTurnAsync("b");
        await t.Engine.ResetProfileAsync();
        Assert.Equal(TurnStatuses.Cancelled, (await a).Status);
        Assert.Equal(TurnStatuses.Cancelled, (await b).Status);
        Assert.Equal(new[] { "a" }, runner.Requests.Select(r => r.Transcript));
    }

    // S21: invalid UTF-16

    [Theory]
    [InlineData("{\"action\":\"ok\",\"text\":\"\\ud800\"}")]
    [InlineData("{\"action\":\"ok\",\"\\udc00\":1}")]
    [InlineData("{\"action\":\"ok\",\"text\":\"a\\ud800b\"}")]
    public async Task S21_escaped_lone_surrogates_are_invalid_arguments(string json)
    {
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { new ProbeTool() });
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", json);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Single(t.Trace.Entries);
    }

    [Fact]
    public async Task S21_a_raw_lone_surrogate_is_invalid_arguments_and_the_trace_stays_writable()
    {
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { new ProbeTool() });
        t.Engine.BeginTurn("x");
        var json = "{\"action\":\"ok\",\"text\":\"a" + '\ud800' + "\"}";
        var r = await t.CallAsync("test_probe", json);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        var entry = t.Trace.Entries.Single();
        Assert.Contains("\uFFFD", entry.Arguments);
        Assert.NotNull(System.Text.Json.JsonSerializer.Serialize(entry, Sensei.Engine.Json.SenseiJsonContext.Default.TraceEntry));
    }

    [Fact]
    public async Task S21_a_valid_surrogate_pair_is_accepted()
    {
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { new ProbeTool() });
        t.Engine.BeginTurn("x");
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_probe", "{\"action\":\"ok\",\"text\":\"\\ud842\\udfb7\"}")).Status);
    }

    // S24: duplicate while the first call runs

    [Fact]
    public async Task S24_the_same_call_id_while_the_first_call_runs_executes_once()
    {
        var mutate = new MutatingProbeTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { mutate });
        t.Engine.BeginTurn("x");
        var first = t.CallAsync("test_mutate", """{"action":"spawn_slow","delay_ms":300}""", "same");
        while (t.Engine.GetScene().Instances.Count == 0) await Task.Delay(5);
        var second = await t.CallAsync("test_mutate", """{"action":"spawn_slow","delay_ms":300}""", "same");
        Assert.Same(await first, second);
        Assert.Equal(1, mutate.Executions);
        Assert.Single(t.Engine.GetScene().Instances);
        Assert.Equal(new[] { false, true }, t.Trace.Entries.Select(e => e.Duplicate));
    }

    [Fact]
    public async Task S82_a_different_call_with_a_running_call_id_is_call_id_reused()
    {
        var mutate = new MutatingProbeTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { mutate });
        t.Engine.BeginTurn("x");
        var first = t.CallAsync("test_mutate", """{"action":"spawn_slow","delay_ms":300}""", "same");
        while (t.Engine.GetScene().Instances.Count == 0) await Task.Delay(5);
        var other = await t.CallAsync("test_mutate", """{"action":"ok"}""", "same");
        Assert.Equal(ToolDetails.CallIdReused, other.Detail);
        Assert.Equal(ToolStatuses.Success, (await first).Status);
    }

    // S87 + S20: reset tells the renderer the task is gone

    [Fact]
    public async Task S87_reset_raises_task_changed_and_the_scene_snapshot_has_no_task()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        lock (t.Core.Sync)
        {
            t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "x" }, new[] { new SkillTarget { Dimension = "listening", Key = "ringo" } },
                new[] { new TaskPredicate { Type = "in_region", ObjectId = a, Region = "left" } }, Array.Empty<ScenarioProperty>(), null, false, 0);
        }
        t.Core.FlushNotifications();
        t.Events.Clear();
        await t.Engine.ResetProfileAsync();
        Assert.Equal(TaskStatuses.Abandoned, t.Events.Tasks.Single().Status);
        Assert.Null(t.Events.SceneSnapshots.Single().ActiveTask);
    }

    [Fact]
    public async Task S87_reset_with_an_empty_scene_still_reports_the_cleared_task()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        lock (t.Core.Sync)
        {
            t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "x" }, new[] { new SkillTarget { Dimension = "listening", Key = "ringo" } },
                new[] { new TaskPredicate { Type = "in_region", ObjectId = a, Region = "left" } }, Array.Empty<ScenarioProperty>(), null, false, 0);
            // test-only shortcut: empty the scene underneath the task
            t.Core.World.CommitRemove(new[] { a }, SceneActors.Sensei, 0);
        }
        t.Core.FlushNotifications();
        t.Events.Clear();
        await t.Engine.ResetProfileAsync();
        Assert.Empty(t.Events.SceneEvents);
        Assert.Equal(TaskStatuses.Abandoned, t.Events.Tasks.Single().Status);
    }

    // S69: events stay with the operation that committed them

    [Fact]
    public async Task S69_another_thread_never_raises_a_running_tool_calls_events()
    {
        var events = new RecordingEvents();
        var mutate = new MutatingProbeTool();
        await using var t = await TestEngine.CreateAsync(events: events, extraTools: new SenseiTool[] { mutate });
        t.Engine.BeginTurn("x");
        var call = t.CallAsync("test_mutate", """{"action":"spawn_slow","delay_ms":300}""");
        while (t.Engine.GetScene().Instances.Count == 0) await Task.Delay(5);
        var playerThread = await Task.Run(() =>
        {
            t.Engine.PlayerSelect("nope_1");
            return Environment.CurrentManagedThreadId;
        });
        Assert.Empty(events.SceneEvents);
        await call;
        var spawned = events.All.Single(e => e.Method == nameof(IEngineEvents.OnSceneChanged));
        Assert.Equal(SceneEventKinds.Spawned, events.SceneEvents.Single().Kind);
        Assert.NotEqual(0, spawned.ThreadId);
        _ = playerThread;
    }

    // S9: the trace sink is called outside the engine lock

    private sealed class LockProbeSink : ITraceSink
    {
        public object? Lock { get; set; }
        public int CalledUnderLock;
        public int Calls;

        public void Write(TraceEntry entry)
        {
            Calls++;
            if (Lock is not null && Monitor.IsEntered(Lock)) CalledUnderLock++;
        }
    }

    [Fact]
    public async Task S9_trace_sink_is_never_called_under_the_engine_lock()
    {
        var sink = new LockProbeSink();
        var store = new InMemoryProfileStore();
        var options = new SenseiEngineOptions { ChatClient = null, Speech = new RecordingSpeechOutput(), ProfileStore = store, Trace = sink };
        await using var engine = await SenseiEngine.CreateAsync(options, new SenseiTool[] { new ProbeTool(), new MutatingProbeTool() }, null);
        sink.Lock = engine.Core.Sync;
        await engine.InvokeToolAsync("test_probe", """{"action":"ok"}""");
        engine.BeginTurn("x");
        await engine.InvokeToolAsync("test_probe", """{"action":"ok"}""", "c1");
        await engine.InvokeToolAsync("test_probe", """{"action":"ok"}""", "c1");
        await engine.InvokeToolAsync("test_mutate", """{"action":"spawn"}""");
        await engine.InvokeToolAsync("test_probe", "{");
        Assert.Equal(5, sink.Calls);
        Assert.Equal(0, sink.CalledUnderLock);
    }

    // S57: parse-valid but malformed profiles are corrupt

    [Theory]
    [InlineData("""{"version":1,"profile_id":"default","support":null}""")]
    [InlineData("""{"version":1,"profile_id":"default","pinned":null}""")]
    [InlineData("""{"version":1,"profile_id":"default","working_level":"n9"}""")]
    [InlineData("""{"version":1,"profile_id":"default","starting_band":"expert"}""")]
    [InlineData("""{"version":1,"profile_id":"default","pinned":["volume"]}""")]
    [InlineData("""{"version":1,"profile_id":"default","support":{"pace":"fast"}}""")]
    [InlineData("""{"version":1,"profile_id":"default","sessions":[null]}""")]
    public async Task S57_a_profile_with_invalid_fields_is_corrupt_and_left_untouched(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sensei-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "default.json");
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
            var store = new FileProfileStore(dir);
            await Assert.ThrowsAsync<ProfileCorruptException>(() => store.LoadAsync("default", CancellationToken.None));
            var options = new SenseiEngineOptions { ChatClient = null, Speech = new RecordingSpeechOutput(), ProfileStore = store };
            await Assert.ThrowsAsync<ProfileCorruptException>(() => SenseiEngine.CreateAsync(options));
            Assert.Equal(json, await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task S57_an_engine_refuses_a_malformed_profile_from_any_store()
    {
        var store = new InMemoryProfileStore();
        var bad = LearnerProfile.CreateDefault("default", DateTimeOffset.UnixEpoch);
        bad.WorkingLevel = "n0";
        store.Put(bad);
        await Assert.ThrowsAsync<ProfileCorruptException>(() => TestEngine.CreateAsync(store: store));
    }

    [Fact]
    public void S58_a_default_profile_has_a_valid_shape() =>
        Assert.Null(LearnerProfile.CreateDefault("default", DateTimeOffset.UnixEpoch).FindShapeProblem());
}

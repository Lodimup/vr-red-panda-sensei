using System.Text.Json;
using Sensei.Engine.Events;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>The shared tool pipeline (S21–S28, S81–S86, S88, S114), exercised through the test-only probe tools.</summary>
public sealed class PipelineTests
{
    private readonly ProbeTool _probe = new();
    private readonly MutatingProbeTool _mutate = new();

    private Task<TestEngine> NewAsync(RecordingEvents? events = null, IReadOnlyList<string>? secrets = null) =>
        TestEngine.CreateAsync(extraTools: new SenseiTool[] { _probe, _mutate }, events: events, knownSecrets: secrets);

    private static Task<ToolResult> Ok(TestEngine t, string? callId = null, string text = "hi") =>
        t.CallAsync("test_probe", $$"""{"action":"ok","text":"{{text}}"}""", callId);

    // ---------------------------------------------------------------- S23 turns

    [Fact]
    public async Task S23_tool_call_before_any_turn_is_no_turn()
    {
        await using var t = await NewAsync();
        var r = await Ok(t);
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.NoTurn, r.ErrorCode);
        Assert.Equal(0, _probe.Executions);
    }

    [Fact]
    public async Task S23_begin_turn_numbers_turns_from_1_and_records_the_utterance()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("こんにちは");
        Assert.Equal(1, t.Core.CurrentTurnId);
        t.Engine.BeginTurn("りんご");
        Assert.Equal(2, t.Core.CurrentTurnId);
        lock (t.Core.Sync)
        {
            Assert.Equal(new[] { "こんにちは", "りんご" }, t.Core.Utterances.All.Select(u => u.Text));
            Assert.Equal(new[] { "l1", "l2" }, t.Core.Utterances.All.Select(u => u.Id));
            Assert.All(t.Core.Utterances.All, u => Assert.False(u.IsSynthetic));
        }
    }

    [Fact]
    public async Task S23_begin_turn_sets_a_new_generation_each_time()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("a");
        var g1 = t.Core.CurrentTurn!.GenerationId;
        t.Engine.BeginTurn("b");
        Assert.True(t.Core.CurrentTurn!.GenerationId > g1);
    }

    [Fact]
    public async Task S92_cli_turns_are_synthetic_utterances()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("(cli)");
        lock (t.Core.Sync) Assert.True(t.Core.Utterances.All.Single().IsSynthetic);
    }

    [Fact]
    public async Task S23_successful_call_inside_a_turn_executes()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await Ok(t);
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Null(r.ErrorCode);
        Assert.Null(r.Detail);
        Assert.Equal("test_probe", r.Tool);
        Assert.Equal("hi", r.Data!.Value.GetProperty("echo").GetString());
    }

    // ---------------------------------------------------------------- S24 / S82 idempotency

    [Fact]
    public async Task S82_engine_assigned_call_ids_are_auto_n()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        Assert.Equal("auto-1", (await Ok(t)).CallId);
        Assert.Equal("auto-2", (await Ok(t)).CallId);
        Assert.Equal("c7", (await Ok(t, "c7")).CallId);
    }

    [Fact]
    public async Task S24_repeated_call_id_returns_the_cached_result_without_executing()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var first = await Ok(t, "c1");
        var second = await Ok(t, "c1");
        Assert.Same(first, second);
        Assert.Equal(1, _probe.Executions);
    }

    [Fact]
    public async Task S24_cached_duplicate_creates_no_events_and_no_revision()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var first = await t.CallAsync("test_mutate", """{"action":"spawn"}""", "s1");
        var revision = t.Engine.GetScene().Revision;
        var events = t.Core.World.Events.Count;
        var again = await t.CallAsync("test_mutate", """{"action":"spawn"}""", "s1");
        Assert.Same(first, again);
        Assert.Equal(revision, t.Engine.GetScene().Revision);
        Assert.Equal(events, t.Core.World.Events.Count);
        Assert.Single(t.Engine.GetScene().Instances);
    }

    [Fact]
    public async Task S24_cached_duplicates_do_not_count_against_the_budget()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await Ok(t, "c1");
        for (var i = 0; i < 20; i++) await Ok(t, "c1");
        for (var i = 0; i < 7; i++) Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, (await Ok(t)).ErrorCode);
    }

    [Fact]
    public async Task S82_argument_order_and_whitespace_do_not_break_the_cache()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var first = await t.CallAsync("test_probe", """{"action":"ok","text":"a"}""", "c1");
        var second = await t.CallAsync("test_probe", """ { "text" : "a" , "action" : "ok" } """, "c1");
        Assert.Same(first, second);
    }

    [Fact]
    public async Task S82_same_call_id_with_different_arguments_is_call_id_reused()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var first = await Ok(t, "c1", "a");
        var reused = await Ok(t, "c1", "b");
        Assert.Equal(ToolErrorCodes.InvalidArguments, reused.ErrorCode);
        Assert.Equal(ToolDetails.CallIdReused, reused.Detail);
        Assert.Same(first, await Ok(t, "c1", "a"));
        Assert.Equal(1, _probe.Executions);
    }

    [Fact]
    public async Task S82_same_call_id_with_a_different_tool_is_call_id_reused()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await t.CallAsync("test_probe", """{"action":"ok"}""", "c1");
        var reused = await t.CallAsync("test_mutate", """{"action":"ok"}""", "c1");
        Assert.Equal(ToolDetails.CallIdReused, reused.Detail);
    }

    [Fact]
    public async Task S82_call_id_reused_counts_as_a_validation_failure()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await Ok(t, "c1", "a");
        await Ok(t, "c1", "b");
        await Ok(t, "c1", "c");
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, (await Ok(t)).ErrorCode);
    }

    [Fact]
    public async Task S82_caller_call_id_starting_with_auto_is_invalid()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var engineAssigned = await Ok(t);
        var r = await Ok(t, engineAssigned.CallId);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.NotSame(engineAssigned, r);
        Assert.Equal(1, _probe.Executions);
    }

    [Fact]
    public async Task S82_cache_key_includes_the_turn()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var first = await Ok(t, "c1");
        t.Engine.BeginTurn("y");
        var second = await Ok(t, "c1");
        Assert.NotSame(first, second);
        Assert.Equal(2, _probe.Executions);
    }

    [Fact]
    public async Task S82_repeated_call_after_cancel_generation_is_cancelled_not_cached()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await Ok(t, "c1");
        t.Engine.CancelGeneration();
        var r = await Ok(t, "c1");
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Equal(ToolErrorCodes.Cancelled, r.ErrorCode);
    }

    [Fact]
    public async Task S24_failures_are_cached_too()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var first = await t.CallAsync("test_probe", """{"action":"fail_unknown"}""", "f1");
        var second = await t.CallAsync("test_probe", """{"action":"fail_unknown"}""", "f1");
        Assert.Same(first, second);
        // only one validation failure counted: a second distinct failure is needed to exhaust
        Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
    }

    // ---------------------------------------------------------------- S25 budget

    [Fact]
    public async Task S25_eight_calls_succeed_and_the_ninth_is_turn_budget_exhausted()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        for (var i = 0; i < 8; i++) Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
        var ninth = await Ok(t);
        Assert.Equal(ToolStatuses.Failure, ninth.Status);
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, ninth.ErrorCode);
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, (await Ok(t)).ErrorCode);
        Assert.Equal(8, _probe.Executions);
    }

    [Fact]
    public async Task S25_failed_calls_count_toward_the_eight()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await t.CallAsync("test_probe", """{"action":"fail_speech"}""");
        for (var i = 0; i < 7; i++) Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, (await Ok(t)).ErrorCode);
    }

    [Fact]
    public async Task S25_one_validation_failure_leaves_the_turn_usable()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await t.CallAsync("test_probe", """{"action":"fail_unknown"}""");
        Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
    }

    [Fact]
    public async Task S25_second_validation_failure_exhausts_the_turn()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await t.CallAsync("test_probe", """{"action":"fail_unknown"}""");
        await Ok(t);
        var second = await t.CallAsync("test_probe", """{"action":"nope"}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, second.ErrorCode);
        var third = await Ok(t);
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, third.ErrorCode);
    }

    [Fact]
    public async Task S25_unknown_tool_counts_as_a_validation_failure()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await t.CallAsync("no_such_tool", "{}");
        await t.CallAsync("no_such_tool", "{}");
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, (await Ok(t)).ErrorCode);
    }

    [Fact]
    public async Task S25_speech_failed_and_stale_scene_are_not_validation_failures()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await t.CallAsync("test_probe", """{"action":"fail_speech"}""");
        await t.CallAsync("test_probe", """{"action":"fail_speech"}""");
        await t.CallAsync("test_probe", """{"action":"fail_speech"}""");
        SpawnAsPlayerVisible(t, out var id);
        t.Engine.PlayerMove(id, SceneRegions.Left, null);
        Assert.Equal(ToolErrorCodes.StaleScene, (await t.CallAsync("test_mutate", """{"action":"ok"}""")).ErrorCode);
        Assert.Equal(ToolErrorCodes.StaleScene, (await t.CallAsync("test_mutate", """{"action":"ok"}""")).ErrorCode);
        Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
    }

    [Fact]
    public async Task S25_budgets_reset_on_the_next_turn()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        for (var i = 0; i < 9; i++) await Ok(t);
        t.Engine.BeginTurn("y");
        Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
    }

    // ---------------------------------------------------------------- S21 strict arguments, S83, S114

    [Theory]
    [InlineData("{", "malformed")]
    [InlineData("[1,2]", "arguments")]
    [InlineData("\"text\"", "arguments")]
    [InlineData("{\"action\":\"ok\",\"extra\":1}", "extra")]
    [InlineData("{}", "action")]
    [InlineData("{\"action\":5}", "action")]
    [InlineData("{\"action\":\"ok\",\"count\":\"2\"}", "count")]
    [InlineData("{\"action\":\"ok\",\"count\":2.5}", "count")]
    [InlineData("{\"action\":\"ok\",\"count\":0}", "count")]
    [InlineData("{\"action\":\"ok\",\"count\":6}", "count")]
    [InlineData("{\"action\":\"ok\",\"flag\":\"true\"}", "flag")]
    [InlineData("{\"action\":\"ok\",\"ids\":\"a\"}", "ids")]
    [InlineData("{\"action\":\"ok\",\"ids\":[1]}", "ids[0]")]
    [InlineData("{\"action\":\"ok\",\"ids\":[\"a\",\"b\",\"c\",\"d\",\"e\"]}", "ids")]
    [InlineData("{\"action\":\"ok\",\"nested\":{}}", "nested.flag")]
    [InlineData("{\"action\":\"ok\",\"nested\":{\"flag\":true,\"x\":1}}", "nested.x")]
    [InlineData("{\"action\":null}", "action")]
    [InlineData("{\"action\":\"ok\",\"text\":\"bad\"}", "text")]
    public async Task S21_invalid_arguments_name_the_offending_property(string json, string expectedInDetail)
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", json);
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Contains(expectedInDetail, r.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _probe.Executions);
    }

    [Fact]
    public async Task S21_enum_values_match_exactly_and_the_detail_lists_allowed_values()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", """{"action":"OK"}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Contains("ok, fail_unknown", r.Detail);
    }

    [Fact]
    public async Task S21_length_limits_count_unicode_scalar_values()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var tenWide = string.Concat(Enumerable.Repeat("𠮷", 10));
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_probe", $$"""{"action":"ok","text":"{{tenWide}}"}""")).Status);
        var eleven = tenWide + "a";
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("test_probe", $$"""{"action":"ok","text":"{{eleven}}"}""")).ErrorCode);
    }

    [Fact]
    public async Task S21_range_boundaries_are_inclusive()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_probe", """{"action":"ok","count":1}""")).Status);
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_probe", """{"action":"ok","count":5}""")).Status);
    }

    [Fact]
    public async Task S21_an_empty_object_is_valid_json_and_names_the_missing_property()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", "{}");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Contains("action", r.Detail);
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("abcd", false)]
    [InlineData("ab\\n", false)]
    [InlineData("AB", false)]
    public async Task S21_patterns_match_the_whole_string(string code, bool ok)
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", "{\"action\":\"ok\",\"code\":\"" + code + "\"}");
        Assert.Equal(ok ? ToolStatuses.Success : ToolStatuses.Failure, r.Status);
        if (!ok) Assert.Contains("code", r.Detail);
    }

    [Fact]
    public async Task S83_duplicate_property_names_are_invalid_at_any_depth()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        t.Engine.BeginTurn("y");
        var top = await t.CallAsync("test_probe", """{"action":"ok","action":"ok"}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, top.ErrorCode);
        Assert.Contains("duplicate", top.Detail);
        t.Engine.BeginTurn("z");
        var nested = await t.CallAsync("test_probe", """{"action":"ok","nested":{"flag":true,"flag":false}}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, nested.ErrorCode);
        Assert.Contains("nested.flag", nested.Detail);
    }

    [Fact]
    public async Task S83_id_arrays_must_be_distinct()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", """{"action":"ok","ids":["a","a"]}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal(ToolDetails.DuplicateId, r.Detail);
    }

    [Fact]
    public async Task S114_explicit_null_for_an_optional_property_is_the_same_as_omitting_it()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", """{"action":"ok","text":null,"count":null,"ids":null,"nested":null,"flag":null}""");
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.False(r.Data!.Value.GetProperty("flag").GetBoolean());
        Assert.Empty(r.AffectedIds);
    }

    [Fact]
    public async Task S113_omitted_optional_boolean_defaults_to_false_and_array_to_empty()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", """{"action":"ok"}""");
        Assert.False(r.Data!.Value.GetProperty("flag").GetBoolean());
        Assert.Empty(r.AffectedIds);
    }

    // ---------------------------------------------------------------- S81 order

    [Fact]
    public async Task S81_cancelled_comes_before_everything()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        t.Engine.CancelGeneration();
        Assert.Equal(ToolStatuses.Cancelled, (await t.CallAsync("no_such_tool", "{")).Status);
    }

    [Fact]
    public async Task S81_no_turn_comes_before_unknown_tool_and_json()
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolErrorCodes.NoTurn, (await t.CallAsync("no_such_tool", "{")).ErrorCode);
    }

    [Fact]
    public async Task S81_exhausted_comes_before_unknown_tool()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        for (var i = 0; i < 8; i++) await Ok(t);
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, (await t.CallAsync("no_such_tool", "{")).ErrorCode);
    }

    [Fact]
    public async Task S81_unknown_tool_comes_before_json_syntax()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        Assert.Equal(ToolErrorCodes.UnknownTool, (await t.CallAsync("no_such_tool", "{")).ErrorCode);
    }

    [Fact]
    public async Task S81_schema_comes_before_same_round_dependency()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var spawned = await t.CallAsync("test_mutate", """{"action":"spawn"}""");
        var r = await t.CallAsync("test_probe", $$"""{"action":"ok","ids":["{{spawned.AffectedIds[0]}}"],"count":9}""");
        Assert.Contains("count", r.Detail);
    }

    [Fact]
    public async Task S81_same_round_dependency_comes_before_stale_scene()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        SpawnAsPlayerVisible(t, out var id);
        var spawned = await t.CallAsync("test_mutate", """{"action":"spawn"}""");
        t.Engine.PlayerMove(id, SceneRegions.Left, null);
        var r = await t.CallAsync("test_mutate", $$"""{"action":"ok","ids":["{{spawned.AffectedIds[0]}}"]}""");
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
    }

    [Fact]
    public async Task S81_stale_scene_comes_before_the_tool_semantic_checks()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        SpawnAsPlayerVisible(t, out var id);
        t.Engine.PlayerMove(id, SceneRegions.Left, null);
        var r = await t.CallAsync("test_mutate", """{"action":"fail_unknown"}""");
        Assert.Equal(ToolErrorCodes.StaleScene, r.ErrorCode);
        Assert.Equal(0, _mutate.Executions);
    }

    // ---------------------------------------------------------------- S84 same-round visibility

    [Fact]
    public async Task S84_an_id_spawned_in_this_round_is_not_yet_visible()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var spawned = await t.CallAsync("test_mutate", """{"action":"spawn"}""");
        var r = await t.CallAsync("test_probe", $$"""{"action":"ok","ids":["{{spawned.AffectedIds[0]}}"]}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
    }

    [Fact]
    public async Task S84_the_id_is_visible_after_the_next_round_starts()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var spawned = await t.CallAsync("test_mutate", """{"action":"spawn"}""");
        t.Engine.MarkRoundStart();
        var r = await t.CallAsync("test_probe", $$"""{"action":"ok","ids":["{{spawned.AffectedIds[0]}}"]}""");
        Assert.Equal(ToolStatuses.Success, r.Status);
    }

    [Fact]
    public async Task S84_ids_that_existed_before_the_round_and_unknown_ids_pass_to_the_tool()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        SpawnAsPlayerVisible(t, out var id);
        var r = await t.CallAsync("test_probe", $$"""{"action":"ok","ids":["{{id}}","ghost_9"]}""");
        Assert.Equal(ToolStatuses.Success, r.Status);
    }

    // ---------------------------------------------------------------- S26 stale scene

    [Fact]
    public async Task S26_player_move_after_round_start_makes_mutating_calls_stale_with_a_fresh_snapshot()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        SpawnAsPlayerVisible(t, out var id);
        t.Engine.PlayerMove(id, SceneRegions.Right, null);
        var r = await t.CallAsync("test_mutate", """{"action":"spawn"}""");
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.StaleScene, r.ErrorCode);
        var data = r.Data!.Value;
        Assert.Equal(t.Engine.GetScene().Revision, data.GetProperty("revision").GetInt64());
        Assert.Equal("right", data.GetProperty("instances")[0].GetProperty("region").GetString());
        Assert.Single(t.Engine.GetScene().Instances);
    }

    [Fact]
    public async Task S26_non_mutating_tools_are_never_stale()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        SpawnAsPlayerVisible(t, out var id);
        t.Engine.PlayerMove(id, SceneRegions.Right, null);
        Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
    }

    [Fact]
    public async Task S26_sensei_own_calls_in_the_round_never_make_it_stale()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_mutate", """{"action":"spawn"}""")).Status);
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_mutate", """{"action":"spawn"}""")).Status);
    }

    [Fact]
    public async Task S26_player_select_does_not_make_the_scene_stale()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        SpawnAsPlayerVisible(t, out var id);
        t.Engine.PlayerSelect(id);
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_mutate", """{"action":"spawn"}""")).Status);
    }

    [Fact]
    public async Task S26_a_player_move_before_the_round_began_is_not_stale()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        SpawnAsPlayerVisible(t, out var id);
        t.Engine.PlayerMove(id, SceneRegions.Right, null);
        t.Engine.MarkRoundStart();
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_mutate", """{"action":"spawn"}""")).Status);
    }

    [Fact]
    public async Task S26_begin_turn_resets_the_round_base()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        SpawnAsPlayerVisible(t, out var id);
        t.Engine.PlayerMove(id, SceneRegions.Right, null);
        t.Engine.BeginTurn("y");
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_mutate", """{"action":"spawn"}""")).Status);
    }

    // ---------------------------------------------------------------- S27 cancellation, S22 no mutation, S88

    [Fact]
    public async Task S27_after_cancel_generation_calls_are_cancelled_until_the_next_turn()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        t.Engine.CancelGeneration();
        var r = await t.CallAsync("test_mutate", """{"action":"spawn"}""");
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Equal(ToolErrorCodes.Cancelled, r.ErrorCode);
        Assert.Empty(t.Engine.GetScene().Instances);
        Assert.Equal(0, _mutate.Executions);
        t.Engine.BeginTurn("y");
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_mutate", """{"action":"spawn"}""")).Status);
    }

    [Fact]
    public async Task S27_an_in_flight_call_is_cancelled_and_changes_nothing()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var call = t.CallAsync("test_mutate", """{"action":"slow","delay_ms":30000}""");
        await WaitUntil(() => _mutate.Executions == 1);
        t.Engine.CancelGeneration();
        var r = await call;
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Equal(0, t.Engine.GetScene().Revision);
    }

    [Fact]
    public async Task S27_cancelled_calls_do_not_count_as_validation_failures()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        t.Engine.CancelGeneration();
        await Ok(t);
        await Ok(t);
        await Ok(t);
        t.Engine.BeginTurn("y");
        Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
    }

    [Fact]
    public async Task S22_a_failed_call_changes_nothing()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var before = t.Core.World.Events.Count;
        var saves = t.Store.SaveCount;
        var r = await t.CallAsync("test_mutate", """{"action":"fail_unknown"}""");
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(0, r.Revision);
        Assert.Equal(before, t.Core.World.Events.Count);
        Assert.Equal(saves, t.Store.SaveCount);
        lock (t.Core.Sync) Assert.Empty(t.Core.Assistance.Records);
    }

    [Fact]
    public async Task S88_end_of_generation_cancels_later_calls_of_the_same_generation()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var end = await t.CallAsync("test_probe", """{"action":"end_generation"}""");
        Assert.Equal(ToolStatuses.Success, end.Status);
        Assert.Equal(ToolStatuses.Cancelled, (await Ok(t)).Status);
        t.Engine.BeginTurn("y");
        Assert.Equal(ToolStatuses.Success, (await Ok(t)).Status);
    }

    [Fact]
    public async Task S82_explicit_older_generation_is_cancelled()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var old = t.Core.CurrentTurn!.GenerationId;
        t.Engine.BeginTurn("y");
        var r = await t.Core.Pipeline.InvokeAsync("test_probe", """{"action":"ok"}""", null, old, CancellationToken.None);
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
    }

    // ---------------------------------------------------------------- result shape, revision

    [Fact]
    public async Task S14_result_revision_is_the_scene_revision_after_the_call()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        Assert.Equal(1, (await t.CallAsync("test_mutate", """{"action":"spawn"}""")).Revision);
        Assert.Equal(1, (await Ok(t)).Revision);
        Assert.Equal(2, (await t.CallAsync("test_mutate", """{"action":"spawn"}""")).Revision);
    }

    [Fact]
    public async Task S6_tool_result_serializes_snake_case_with_every_field()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", """{"action":"fail_unknown"}""", "c1");
        var json = JsonSerializer.SerializeToElement(r, Sensei.Engine.Json.SenseiJsonContext.Default.ToolResult);
        Assert.Equal("c1", json.GetProperty("call_id").GetString());
        Assert.Equal("test_probe", json.GetProperty("tool").GetString());
        Assert.Equal("failure", json.GetProperty("status").GetString());
        Assert.Equal("unknown_object", json.GetProperty("error_code").GetString());
        Assert.Equal("probe failure", json.GetProperty("detail").GetString());
        Assert.Equal(0, json.GetProperty("revision").GetInt64());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("affected_ids").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("data").ValueKind);
    }

    // ---------------------------------------------------------------- S28 / S86 trace, S77 sanitizing

    [Fact]
    public async Task S28_every_call_writes_one_trace_entry_including_failures_cancelled_and_duplicates()
    {
        await using var t = await NewAsync();
        await Ok(t);                                   // no_turn
        t.Engine.BeginTurn("x");
        await Ok(t, "c1");                             // success
        await Ok(t, "c1");                             // duplicate
        await t.CallAsync("test_probe", "{");          // invalid
        t.Engine.CancelGeneration();
        await Ok(t);                                   // cancelled
        var entries = t.Trace.Entries;
        Assert.Equal(5, entries.Count);
        Assert.Equal(new[] { "failure", "success", "success", "failure", "cancelled" }, entries.Select(e => e.Status));
        Assert.Equal(new[] { false, false, true, false, false }, entries.Select(e => e.Duplicate));
        Assert.Equal(ToolErrorCodes.NoTurn, entries[0].ErrorCode);
        Assert.Equal(ToolErrorCodes.Cancelled, entries[4].ErrorCode);
    }

    [Fact]
    public async Task S28_trace_entry_fields_are_filled()
    {
        await using var t = await NewAsync();
        t.Clock.Set(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero));
        t.Engine.BeginTurn("x");
        t.Engine.MarkRoundStart();
        var r = await t.CallAsync("test_mutate", """{"action":"spawn"}""", "c9");
        var e = t.Trace.Entries.Single();
        Assert.Equal(t.Engine.SessionId, e.SessionId);
        Assert.Equal(1, e.TurnId);
        Assert.Equal(t.Core.CurrentTurn!.GenerationId, e.GenerationId);
        Assert.Equal(2, e.Round);
        Assert.Equal("c9", e.CallId);
        Assert.Equal("test_mutate", e.Tool);
        Assert.Equal("""{"action":"spawn"}""", e.Arguments);
        Assert.Equal("success", e.Status);
        Assert.Null(e.ErrorCode);
        Assert.Equal(1, e.Revision);
        Assert.True(e.ElapsedMs >= 0);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero), e.At);
        Assert.Same(r, e.Result);
    }

    [Fact]
    public async Task S86_trace_result_is_the_result_returned_to_the_caller()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", """{"action":"fail_unknown"}""");
        Assert.Same(r, t.Trace.Entries.Single().Result);
    }

    [Fact]
    public async Task S28_trace_entries_of_the_turn_are_kept_on_the_turn()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await Ok(t);
        await Ok(t);
        lock (t.Core.Sync) Assert.Equal(2, t.Core.CurrentTurn!.TraceEntries.Count);
    }

    [Fact]
    public async Task S77_known_secrets_are_redacted_from_trace_arguments_call_id_and_detail()
    {
        await using var t = await NewAsync(secrets: new[] { "zq9secret" });
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", """{"action":"secret","text":"zq9secret"}""", "id-zq9secret");
        Assert.DoesNotContain("zq9secret", r.Detail);
        Assert.Contains("[redacted]", r.Detail);
        Assert.DoesNotContain("zq9secret", r.CallId);
        var e = t.Trace.Entries.Single();
        Assert.DoesNotContain("zq9secret", e.Arguments);
        Assert.DoesNotContain("zq9secret", e.CallId);
        Assert.Contains("[redacted]", e.Arguments);
    }

    [Fact]
    public async Task S77_credential_pattern_is_redacted_from_trace_arguments_and_result_data()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        var key = "sk-" + new string('a', 20);
        var r = await t.CallAsync("test_probe", $$"""{"action":"ok","text":"{{key}}"}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode); // longer than 10 characters
        Assert.DoesNotContain(key, t.Trace.Entries.Single().Arguments);
        Assert.DoesNotContain(key, r.Detail ?? "");
    }

    [Fact]
    public async Task S131_malformed_arguments_are_replaced_in_the_trace_not_stored_as_raw_text()
    {
        await using var t = await NewAsync(secrets: new[] { "zq9secret" });
        t.Engine.BeginTurn("x");
        await t.CallAsync("test_probe", "{zq9secret");
        var arguments = t.Trace.Entries.Single().Arguments;
        Assert.Equal("\"[unparseable arguments]\"", arguments);
        Assert.DoesNotContain("zq9secret", arguments);
    }

    // ---------------------------------------------------------------- exceptions, events

    [Fact]
    public async Task S28_an_exception_inside_a_tool_is_traced_and_rethrown()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.CallAsync("test_probe", """{"action":"throw"}"""));
        var e = t.Trace.Entries.Single();
        Assert.Equal("failure", e.Status);
        Assert.Equal(ToolErrorCodes.InternalError, e.ErrorCode);
    }

    [Fact]
    public async Task S69_event_handler_exception_is_traced_and_the_call_still_succeeds()
    {
        var events = new RecordingEvents { ThrowFrom = nameof(IEngineEvents.OnHint) };
        await using var t = await NewAsync(events);
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_probe", """{"action":"hint","text":"x"}""");
        Assert.Equal(ToolStatuses.Success, r.Status);
        var handlerError = t.Trace.Entries.Single(e => e.Tool == "event:OnHint");
        Assert.Equal("failure", handlerError.Status);
        Assert.Equal(ToolErrorCodes.EventHandlerError, handlerError.ErrorCode);
        Assert.Single(events.Hints);
    }

    [Fact]
    public async Task S9_tool_calls_and_player_moves_from_many_threads_stay_consistent()
    {
        await using var t = await NewAsync();
        t.Engine.BeginTurn("x");
        SpawnAsPlayerVisible(t, out var id);
        var regions = new[] { SceneRegions.Left, SceneRegions.Center, SceneRegions.Right };
        var moves = Enumerable.Range(0, 200).Select(i => Task.Run(() => t.Engine.PlayerMove(id, regions[i % 3], null))).ToArray();
        var calls = Enumerable.Range(0, 50).Select(_ => Task.Run(() => Ok(t))).ToArray();
        var results = await Task.WhenAll(moves);
        await Task.WhenAll(calls);
        var committed = results.Count(r => r.Ok);
        Assert.Equal(1 + committed, t.Engine.GetScene().Revision);
        var revisions = t.Core.World.Events.Select(e => e.Revision).ToList();
        Assert.Equal(revisions.OrderBy(x => x), revisions);
        Assert.Equal(Enumerable.Range(1, t.Core.World.Events.Count).Select(x => (long)x), t.Core.World.Events.Select(e => e.EventId));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Spawns an apple before the current round so it is visible, then re-marks the round.</summary>
    private static void SpawnAsPlayerVisible(TestEngine t, out string id)
    {
        lock (t.Core.Sync)
        {
            id = t.Core.World.CommitSpawn(new[] { new SpawnRequest("apple", SceneRegions.Center) }, SceneActors.Sensei, t.Core.CurrentTurnId).InstanceIds[0];
        }
        t.Core.FlushNotifications();
        t.Engine.MarkRoundStart();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met");
            await Task.Delay(5);
        }
    }
}

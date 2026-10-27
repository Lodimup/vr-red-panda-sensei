using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.World;

/// <summary>The shared pipeline rules (S22, S24–S28, S81) as they apply to every world tool.</summary>
public sealed class PipelineWorldTests
{
    private static readonly (string Tool, string Json)[] MutatingCalls =
    {
        ("spawn_objects", """{"objects":[{"catalog_id":"pen"}]}"""),
        ("move_object", """{"object_id":"apple_1","destination":{"region":"right"},"purpose":"arrange"}"""),
        ("remove_objects", """{"object_ids":["bag_1"]}"""),
        ("start_challenge", """{"family":"listen_arrange","instruction":{"english":"x"},"skill_targets":[{"dimension":"vocabulary","key":"k"}],"predicates":[{"type":"in_region","object_id":"apple_1","region":"right"}]}"""),
        ("update_challenge", """{"task_id":"t1","action":"abandon","reason":"x"}"""),
    };

    private static readonly (string Tool, string Json)[] ReadingCalls =
    {
        ("inspect_scene", "{}"),
        ("highlight_objects", """{"object_ids":["apple_1"]}"""),
        ("animate_sensei", """{"animation":"point","target_id":"apple_1"}"""),
    };

    public static IEnumerable<object[]> Mutating() => MutatingCalls.Select(c => new object[] { c.Tool, c.Json });

    public static IEnumerable<object[]> Reading() => ReadingCalls.Select(c => new object[] { c.Tool, c.Json });

    public static IEnumerable<object[]> All() => Mutating().Concat(Reading());

    // ------------------------------------------------------------------ S26 stale scene

    [Theory]
    [MemberData(nameof(Mutating))]
    public async Task S26_a_player_move_since_the_round_began_makes_every_mutating_tool_stale(string tool, string json)
    {
        await using var t = await StartAsync(("apple", "left"), ("bag", "center"), ("cup", "right"));
        Assert.True(t.Engine.PlayerMove("cup_1", "left", null).Ok);
        var r = await AssertRefusedAsync(t, tool, json, ToolErrorCodes.StaleScene);
        var snapshot = Data(r);
        Assert.Equal("left", snapshot.GetProperty("instances").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "cup_1").GetProperty("region").GetString());
        Assert.Equal(Revision(t), snapshot.GetProperty("revision").GetInt64());
    }

    [Theory]
    [MemberData(nameof(Reading))]
    public async Task S26_reading_tools_are_never_stale(string tool, string json)
    {
        await using var t = await StartAsync(("apple", "left"), ("bag", "center"), ("cup", "right"));
        Assert.True(t.Engine.PlayerMove("cup_1", "left", null).Ok);
        var r = await t.CallAsync(tool, json);
        Assert.True(r.IsSuccess, r.Detail);
    }

    [Fact]
    public async Task S26_a_new_round_sees_the_player_move_and_the_call_goes_through()
    {
        await using var t = await StartAsync(("apple", "left"), ("cup", "right"));
        t.Engine.PlayerMove("cup_1", "left", null);
        Assert.Equal(ToolErrorCodes.StaleScene, (await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"pen"}]}""")).ErrorCode);
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"pen"}]}""")).IsSuccess);
    }

    [Fact]
    public async Task S26_a_player_selection_alone_does_not_make_the_scene_stale()
    {
        await using var t = await StartAsync(("apple", "left"));
        t.Engine.PlayerSelect("apple_1");
        Assert.True((await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"pen"}]}""")).IsSuccess);
    }

    [Fact]
    public async Task S26_sensei_own_earlier_calls_in_the_round_never_make_the_scene_stale()
    {
        await using var t = await StartAsync(("apple", "left"), ("cup", "right"));
        Assert.True((await t.CallAsync("move_object", MoveJson("apple_1", "arrange", region: "center"))).IsSuccess);
        Assert.True((await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "center"))).IsSuccess);
        Assert.True((await t.CallAsync("remove_objects", Ids("apple_1"))).IsSuccess);
    }

    [Fact]
    public async Task S26_a_stale_call_is_not_a_validation_failure()
    {
        await using var t = await StartAsync(("apple", "left"), ("cup", "right"));
        t.Engine.PlayerMove("cup_1", "left", null);
        for (var i = 0; i < 3; i++)
            Assert.Equal(ToolErrorCodes.StaleScene, (await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"pen"}]}""")).ErrorCode);
        Assert.Equal(ToolErrorCodes.StaleScene, (await t.CallAsync("remove_objects", Ids("apple_1"))).ErrorCode);
    }

    // ------------------------------------------------------------------ S27 cancellation

    [Theory]
    [MemberData(nameof(All))]
    public async Task S27_after_cancel_generation_every_world_tool_is_cancelled_and_changes_nothing(string tool, string json)
    {
        await using var t = await StartAsync(("apple", "left"), ("bag", "center"));
        t.Engine.CancelGeneration();
        var revision = Revision(t);
        var events = SceneLog(t).Count;
        var r = await t.CallAsync(tool, json);
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Equal(ToolErrorCodes.Cancelled, r.ErrorCode);
        Assert.Equal(revision, Revision(t));
        Assert.Equal(events, SceneLog(t).Count);
        Assert.Empty(Aid(t));
        Assert.DoesNotContain(t.Events.Methods, m => m is "OnHighlight" or "OnAnimate" or "OnTaskChanged");
    }

    [Fact]
    public async Task S27_a_new_turn_after_cancellation_works_again()
    {
        await using var t = await StartAsync();
        t.Engine.CancelGeneration();
        Assert.Equal(ToolStatuses.Cancelled, (await t.CallAsync("inspect_scene", "{}")).Status);
        t.Engine.BeginTurn("next");
        Assert.True((await t.CallAsync("inspect_scene", "{}")).IsSuccess);
    }

    // ------------------------------------------------------------------ S23, S24, S25, S28

    [Fact]
    public async Task S23_world_tools_need_a_turn()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"pen"}]}""");
        Assert.Equal(ToolErrorCodes.NoTurn, r.ErrorCode);
        Assert.Empty(Instances(t));
    }

    [Fact]
    public async Task S24_a_cached_duplicate_returns_the_same_result_for_every_kind_of_call()
    {
        await using var t = await StartAsync(("apple", "left"), ("cup", "right"));
        foreach (var (tool, json) in new[]
                 {
                     ("move_object", MoveJson("apple_1", "arrange", region: "center")),
                     ("highlight_objects", Ids("cup_1")),
                     ("animate_sensei", """{"animation":"listen"}"""),
                 })
        {
            var first = await t.CallAsync(tool, json, "same-" + tool);
            var second = await t.CallAsync(tool, json, "same-" + tool);
            Assert.Equal(first.Status, second.Status);
            Assert.Equal(first.Revision, second.Revision);
            Assert.Equal(first.AffectedIds, second.AffectedIds);
        }
        Assert.Single(t.Events.Of<(string, string?)>("OnAnimate"));
        Assert.Single(t.Events.Of<(IReadOnlyList<string>, int)>("OnHighlight"));
        Assert.Single(SceneLog(t), e => e.Kind == "moved");
    }

    [Fact]
    public async Task S25_two_validation_failures_exhaust_the_turn_for_the_world_tools()
    {
        await using var t = await StartAsync(("apple", "left"));
        Assert.Equal(ToolErrorCodes.UnknownCatalogId, (await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"sword"}]}""")).ErrorCode);
        Assert.Equal(ToolErrorCodes.UnknownObject, (await t.CallAsync("highlight_objects", Ids("pen_9"))).ErrorCode);
        var third = await t.CallAsync("inspect_scene", "{}");
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, third.ErrorCode);
        t.Engine.BeginTurn("fresh");
        Assert.True((await t.CallAsync("inspect_scene", "{}")).IsSuccess);
    }

    [Fact]
    public async Task S25_the_ninth_call_of_a_turn_is_turn_budget_exhausted()
    {
        await using var t = await StartAsync(("apple", "left"));
        for (var i = 0; i < 8; i++) Assert.True((await t.CallAsync("inspect_scene", "{}")).IsSuccess);
        var ninth = await t.CallAsync("inspect_scene", "{}");
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, ninth.ErrorCode);
    }

    [Fact]
    public async Task S28_every_world_call_writes_one_trace_entry_with_its_status_and_revision()
    {
        await using var t = await StartAsync(("apple", "left"));
        await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"pen"}]}""");
        await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"sword"}]}""");
        var entries = t.Trace.Entries.Where(e => e.Tool == "spawn_objects").ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(ToolStatuses.Success, entries[0].Status);
        Assert.Equal(2, entries[0].Revision);
        Assert.Equal(ToolStatuses.Failure, entries[1].Status);
        Assert.Equal(ToolErrorCodes.UnknownCatalogId, entries[1].ErrorCode);
        Assert.NotNull(entries[1].Result);
    }

    [Fact]
    public async Task S69_scene_notifications_of_a_world_call_carry_the_post_change_snapshot()
    {
        await using var t = await StartAsync(("cup", "left"));
        t.Events.Clear();
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "right"));
        var e = Assert.Single(t.Events.SceneEvents);
        Assert.Equal("moved", e.Kind);
        var snapshot = Assert.Single(t.Events.SceneSnapshots);
        Assert.Equal("right", snapshot.Instances.Single().Region);
        Assert.Equal(e.Revision, snapshot.Revision);
    }

    [Fact]
    public async Task S9_world_tools_and_player_actions_can_interleave_without_losing_a_revision()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "left"));
        var start = Revision(t);
        var player = Task.Run(() =>
        {
            for (var i = 0; i < 20; i++) t.Engine.PlayerMove("pen_1", i % 2 == 0 ? "right" : "left", null);
        });
        var spawned = 0;
        for (var i = 0; i < 8; i++)
        {
            t.Engine.MarkRoundStart();
            var r = await t.CallAsync("highlight_objects", Ids("cup_1"));
            Assert.True(r.IsSuccess);
            spawned++;
        }
        await player;
        Assert.Equal(8, spawned);
        Assert.Equal(start + 20, Revision(t));
    }
}

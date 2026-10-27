using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.World;

/// <summary>inspect_scene (S29) and spawn_objects (S30a–c, S12, S13, S83), through InvokeToolAsync in a manual turn (S23).</summary>
public sealed class InspectSpawnTests
{
    // ------------------------------------------------------------------ S29 inspect_scene

    [Fact]
    public async Task S29_inspect_scene_returns_the_snapshot_with_names_and_places()
    {
        await using var t = await StartAsync(("apple", "left"), ("box", "right"));
        var r = await t.CallAsync("inspect_scene", "{}");
        Assert.True(r.IsSuccess);
        var data = Data(r);
        Assert.Equal(0, data.GetProperty("revision").GetInt64() - Revision(t));
        var apple = data.GetProperty("instances").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "apple_1");
        Assert.Equal("apple", apple.GetProperty("catalog_id").GetString());
        Assert.Equal("left", apple.GetProperty("region").GetString());
        Assert.Equal(1, apple.GetProperty("slot").GetInt32());
        Assert.Equal("りんご", apple.GetProperty("japanese").GetString());
        Assert.Equal("りんご", apple.GetProperty("reading").GetString());
        Assert.Equal("apple", apple.GetProperty("english").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, apple.GetProperty("container_id").ValueKind);
        Assert.False(apple.TryGetProperty("variant", out _));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("selected_id").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("active_task").ValueKind);
        Assert.True(data.GetProperty("support").GetProperty("show_japanese").GetBoolean());
        Assert.Equal(2, data.GetProperty("instances").GetArrayLength());
    }

    [Fact]
    public async Task S29_inspect_scene_of_an_empty_scene_has_no_instances()
    {
        await using var t = await StartAsync();
        var r = await t.CallAsync("inspect_scene", "{}");
        Assert.True(r.IsSuccess);
        Assert.Equal(0, Data(r).GetProperty("instances").GetArrayLength());
        Assert.Equal(0, Data(r).GetProperty("revision").GetInt64());
        Assert.Empty(r.AffectedIds);
    }

    [Fact]
    public async Task S29_inspect_scene_never_mutates()
    {
        await using var t = await StartAsync(("apple", "left"));
        var revision = Revision(t);
        var events = SceneLog(t).Count;
        await t.CallAsync("inspect_scene", "{}");
        await t.CallAsync("inspect_scene", "{}");
        Assert.Equal(revision, Revision(t));
        Assert.Equal(events, SceneLog(t).Count);
        Assert.DoesNotContain("OnTaskChanged", t.Events.Methods);
    }

    [Fact]
    public async Task S29_inspect_scene_shows_selection_container_contents_and_active_task()
    {
        await using var t = await StartAsync(("apple", "left"), ("bag", "center"), ("cup", "right"));
        Assert.True(t.Engine.PlayerSelect("cup_1").Ok);
        Assert.True(t.Engine.PlayerMove("apple_1", null, "bag_1").Ok);
        t.Engine.BeginTurn("again");
        await StartTaskAsync(t, Predicates(Obj("cup_1", "left")));
        var r = await t.CallAsync("inspect_scene", "{}");
        var data = Data(r);
        Assert.Equal("cup_1", data.GetProperty("selected_id").GetString());
        var apple = data.GetProperty("instances").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "apple_1");
        Assert.Equal("bag_1", apple.GetProperty("container_id").GetString());
        Assert.Equal("center", apple.GetProperty("region").GetString());
        Assert.Equal("t1", data.GetProperty("active_task").GetProperty("task_id").GetString());
        Assert.Equal(Revision(t), data.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task S29_inspect_scene_is_not_made_stale_by_a_player_move()
    {
        await using var t = await StartAsync(("apple", "left"));
        Assert.True(t.Engine.PlayerMove("apple_1", "right", null).Ok);
        var r = await t.CallAsync("inspect_scene", "{}");
        Assert.True(r.IsSuccess);
        Assert.Equal("right", Data(r).GetProperty("instances")[0].GetProperty("region").GetString());
    }

    [Theory]
    [InlineData("""{"x":1}""", "x: unknown property")]
    [InlineData("""[]""", "")]
    [InlineData("""{""", "malformed")]
    public async Task S21_inspect_scene_takes_no_arguments(string json, string detail)
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "inspect_scene", json, ToolErrorCodes.InvalidArguments, detail.Length == 0 ? null : detail);
    }

    // ------------------------------------------------------------------ S30a defaults

    [Fact]
    public async Task S30a_defaults_are_center_and_quantity_one()
    {
        await using var t = await StartAsync();
        var r = await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"cup"}]}""");
        Assert.True(r.IsSuccess);
        Assert.Equal(new[] { "cup_1" }, r.AffectedIds);
        var cup = Inst(t, "cup_1");
        Assert.Equal("center", cup.Region);
        Assert.Equal(1, cup.Slot);
        Assert.Null(cup.ContainerId);
        Assert.Single(Instances(t));
    }

    [Fact]
    public async Task S30a_an_explicit_region_and_quantity_are_used()
    {
        await using var t = await StartAsync();
        var r = await t.CallAsync("spawn_objects", SpawnJson(("apple", "left", 3)));
        Assert.True(r.IsSuccess);
        Assert.Equal(new[] { "apple_1", "apple_2", "apple_3" }, r.AffectedIds);
        Assert.All(Instances(t), i => Assert.Equal("left", i.Region));
        Assert.Equal(new[] { 1, 2, 3 }, Instances(t).Select(i => i.Slot));
    }

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    [InlineData("sensei_side")]
    public async Task S30a_every_spawnable_region_is_accepted(string region)
    {
        await using var t = await StartAsync();
        var r = await t.CallAsync("spawn_objects", SpawnJson(("pen", region, null)));
        Assert.True(r.IsSuccess);
        Assert.Equal(region, Inst(t, "pen_1").Region);
    }

    [Fact]
    public async Task S15_slots_count_per_practice_region_and_are_ignored_beside_sensei()
    {
        await using var t = await StartAsync();
        await t.CallAsync("spawn_objects", SpawnJson(("pen", "left", null), ("book", "right", null), ("cup", "left", null), ("key", "sensei_side", null)));
        Assert.Equal(1, Inst(t, "pen_1").Slot);
        Assert.Equal(1, Inst(t, "book_1").Slot);
        Assert.Equal(2, Inst(t, "cup_1").Slot);
        Assert.Equal(0, Inst(t, "key_1").Slot);
    }

    // ------------------------------------------------------------------ S30b refusals

    [Fact]
    public async Task S30b_unknown_catalog_id_is_unknown_catalog_id_and_nothing_spawns()
    {
        await using var t = await StartAsync();
        var r = await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("cup", null, null), ("sword", null, null)), ToolErrorCodes.UnknownCatalogId, "objects[1].catalog_id");
        Assert.Contains("sword", r.Detail);
        Assert.Empty(Instances(t));
    }

    [Theory]
    [InlineData("Cup")]
    [InlineData("cup ")]
    [InlineData("")]
    [InlineData("cup_1")]
    public async Task S30b_catalog_ids_match_exactly(string catalogId)
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "spawn_objects", SpawnJson((catalogId, null, null)), ToolErrorCodes.UnknownCatalogId);
    }

    [Fact]
    public async Task S30b_the_learner_hand_is_not_a_spawn_region()
    {
        await using var t = await StartAsync();
        var r = await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("cup", "learner_hand", null)), ToolErrorCodes.InvalidArguments, "objects[0].region");
        Assert.Contains("sensei_side", r.Detail);
    }

    [Fact]
    public async Task S30b_an_unknown_region_name_is_invalid_arguments()
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("cup", "Left", null)), ToolErrorCodes.InvalidArguments, "objects[0].region");
    }

    [Fact]
    public async Task S119_a_variant_property_is_refused_and_unsupported_asset_is_gone()
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "spawn_objects", """{"objects":[{"catalog_id":"cup","variant":"red"}]}""", ToolErrorCodes.InvalidArguments, "objects[0].variant: unknown property");
        Assert.DoesNotContain("unsupported_asset", ToolErrorCodes.All);
    }

    [Fact]
    public async Task S30b_capacity_is_checked_after_catalog_ids_and_spawns_nothing()
    {
        await using var t = await StartAsync();
        await t.CallAsync("spawn_objects", SpawnJson(("apple", "left", 4), ("pen", "left", 2)));
        await t.CallAsync("spawn_objects", SpawnJson(("cup", "center", 4), ("book", "center", 2)));
        await t.CallAsync("spawn_objects", SpawnJson(("key", "right", 2)));
        Assert.Equal(14, Instances(t).Count);
        // 14 + 3 = 17 > 16, and an unknown id in the same call wins over the capacity failure.
        t.Engine.BeginTurn("next");
        await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("sword", null, 1), ("pen", null, 3)), ToolErrorCodes.UnknownCatalogId);
        t.Engine.BeginTurn("next");
        var r = await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("pen", null, 3)), ToolErrorCodes.CapacityExceeded);
        Assert.Equal(14, Instances(t).Count);
        Assert.Contains("16", r.Detail);
    }

    [Fact]
    public async Task S13_exactly_sixteen_instances_fit_and_the_seventeenth_does_not()
    {
        await using var t = await StartAsync();
        Assert.True((await t.CallAsync("spawn_objects", SpawnJson(("apple", "left", 4), ("pen", "left", 2)))).IsSuccess);
        Assert.True((await t.CallAsync("spawn_objects", SpawnJson(("cup", "center", 4), ("book", "center", 2)))).IsSuccess);
        Assert.True((await t.CallAsync("spawn_objects", SpawnJson(("key", "right", 3)))).IsSuccess);
        var last = await t.CallAsync("spawn_objects", SpawnJson(("tea", "right", 1)));
        Assert.True(last.IsSuccess);
        Assert.Equal(16, Instances(t).Count);
        t.Engine.BeginTurn("full");
        await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("water", null, null)), ToolErrorCodes.CapacityExceeded);
    }

    [Fact]
    public async Task S13_objects_inside_containers_and_hands_count_toward_capacity()
    {
        await using var t = await StartAsync(("bag", "center"), ("apple", "left"), ("pen", "left"));
        Assert.True(t.Engine.PlayerMove("apple_1", null, "bag_1").Ok);
        Assert.True(t.Engine.PlayerMove("pen_1", "learner_hand", null).Ok);
        t.Engine.BeginTurn("fill");
        Assert.True((await t.CallAsync("spawn_objects", SpawnJson(("cup", null, 4), ("book", null, 2)))).IsSuccess);
        Assert.True((await t.CallAsync("spawn_objects", SpawnJson(("key", null, 4), ("tea", null, 2)))).IsSuccess);
        Assert.True((await t.CallAsync("spawn_objects", SpawnJson(("water", null, 1)))).IsSuccess);
        Assert.Equal(16, Instances(t).Count);
        t.Engine.BeginTurn("over");
        await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("water", null, null)), ToolErrorCodes.CapacityExceeded);
    }

    // ------------------------------------------------------------------ argument bounds (S30)

    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(-1, false)]
    public async Task S30_quantity_is_one_to_four(int quantity, bool accepted)
    {
        await using var t = await StartAsync();
        var json = SpawnJson(("pen", null, quantity));
        if (accepted) Assert.True((await t.CallAsync("spawn_objects", json)).IsSuccess);
        else await AssertRefusedAsync(t, "spawn_objects", json, ToolErrorCodes.InvalidArguments, "quantity");
    }

    [Fact]
    public async Task S30_a_non_integer_quantity_is_invalid()
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "spawn_objects", """{"objects":[{"catalog_id":"pen","quantity":1.5}]}""", ToolErrorCodes.InvalidArguments, "quantity");
    }

    [Fact]
    public async Task S30_the_objects_list_has_one_to_six_entries()
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "spawn_objects", """{"objects":[]}""", ToolErrorCodes.InvalidArguments, "objects");
        t.Engine.BeginTurn("n");
        var seven = SpawnJson(Enumerable.Repeat(("pen", (string?)null, (int?)null), 7).ToArray());
        await AssertRefusedAsync(t, "spawn_objects", seven, ToolErrorCodes.InvalidArguments, "objects");
        t.Engine.BeginTurn("n");
        var six = SpawnJson(Enumerable.Repeat(("pen", (string?)null, (int?)null), 6).ToArray());
        var r = await t.CallAsync("spawn_objects", six);
        Assert.True(r.IsSuccess);
        Assert.Equal(6, r.AffectedIds.Count);
    }

    [Fact]
    public async Task S30_total_quantity_is_at_most_six()
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("pen", null, 4), ("cup", null, 3)), ToolErrorCodes.InvalidArguments, "quantity");
        Assert.Empty(Instances(t));
        t.Engine.BeginTurn("n");
        var r = await t.CallAsync("spawn_objects", SpawnJson(("pen", null, 4), ("cup", null, 2)));
        Assert.True(r.IsSuccess);
        Assert.Equal(6, r.AffectedIds.Count);
    }

    [Fact]
    public async Task S30_omitted_quantities_count_as_one_toward_the_total()
    {
        await using var t = await StartAsync();
        // 4 + 1 + 1 + 1 = 7 with three defaults; 4 + 1 + 1 = 6 passes.
        await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("pen", null, 4), ("cup", null, null), ("book", null, null), ("key", null, null)), ToolErrorCodes.InvalidArguments, "total quantity");
        t.Engine.BeginTurn("n");
        Assert.True((await t.CallAsync("spawn_objects", SpawnJson(("pen", null, 4), ("cup", null, null), ("book", null, null)))).IsSuccess);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"objects":"cup"}""")]
    [InlineData("""{"objects":[{"region":"left"}]}""")]
    [InlineData("""{"objects":[{"catalog_id":7}]}""")]
    [InlineData("""{"objects":[{"catalog_id":"cup","size":2}]}""")]
    [InlineData("""{"objects":[{"catalog_id":"cup"}],"extra":true}""")]
    [InlineData("""{"objects":[null]}""")]
    public async Task S21_spawn_objects_arguments_are_strict(string json)
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "spawn_objects", json, ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task S114_explicit_null_for_an_optional_property_is_omission()
    {
        await using var t = await StartAsync();
        var r = await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"cup","region":null,"quantity":null}]}""");
        Assert.True(r.IsSuccess);
        Assert.Equal("center", Inst(t, "cup_1").Region);
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "spawn_objects", """{"objects":null}""", ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task S83_duplicate_property_names_in_spawn_arguments_are_invalid()
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "spawn_objects", """{"objects":[{"catalog_id":"cup","catalog_id":"pen"}]}""", ToolErrorCodes.InvalidArguments);
    }

    // ------------------------------------------------------------------ S30c / S12 / S14 / S17 success

    [Fact]
    public async Task S30c_affected_ids_follow_argument_order_then_quantity_order()
    {
        await using var t = await StartAsync();
        var r = await t.CallAsync("spawn_objects", SpawnJson(("cup", "left", null), ("apple", "right", 2), ("cup", "center", null)));
        Assert.True(r.IsSuccess);
        Assert.Equal(new[] { "cup_1", "apple_1", "apple_2", "cup_2" }, r.AffectedIds);
        Assert.Equal(new[] { "cup_1", "apple_1", "apple_2", "cup_2" }, Instances(t).Select(i => i.Id));
    }

    [Fact]
    public async Task S83_spawn_entries_may_repeat_a_catalog_id()
    {
        await using var t = await StartAsync();
        var r = await t.CallAsync("spawn_objects", SpawnJson(("pen", "left", null), ("pen", "right", null)));
        Assert.True(r.IsSuccess);
        Assert.Equal(new[] { "pen_1", "pen_2" }, r.AffectedIds);
        Assert.Equal("left", Inst(t, "pen_1").Region);
        Assert.Equal("right", Inst(t, "pen_2").Region);
    }

    [Fact]
    public async Task S30c_one_call_is_one_spawned_event_and_one_revision()
    {
        await using var t = await StartAsync();
        var before = Revision(t);
        var r = await t.CallAsync("spawn_objects", SpawnJson(("apple", "left", 3)));
        Assert.Equal(before + 1, Revision(t));
        Assert.Equal(before + 1, r.Revision);
        var e = Assert.Single(SceneLog(t));
        Assert.Equal("spawned", e.Kind);
        Assert.Equal("sensei", e.Actor);
        Assert.Equal(new[] { "apple_1", "apple_2", "apple_3" }, e.InstanceIds);
        Assert.Equal(before + 1, e.Revision);
        Assert.Equal(1, e.EventId);
        Assert.Equal(t.Engine.GetScene().Revision, e.Revision);
        var raised = Assert.Single(t.Events.SceneEvents);
        Assert.Equal(e.EventId, raised.EventId);
        Assert.Equal(3, Assert.Single(t.Events.SceneSnapshots).Instances.Count);
    }

    [Fact]
    public async Task S12_ids_count_per_catalog_id_and_are_never_reused_after_removal()
    {
        await using var t = await StartAsync();
        await t.CallAsync("spawn_objects", SpawnJson(("pen", null, 2), ("cup", null, 1)));
        t.Engine.BeginTurn("n");
        Assert.True((await t.CallAsync("remove_objects", Ids("pen_2", "pen_1"))).IsSuccess);
        var r = await t.CallAsync("spawn_objects", SpawnJson(("pen", null, null), ("cup", null, null)));
        Assert.Equal(new[] { "pen_3", "cup_2" }, r.AffectedIds);
    }

    [Fact]
    public async Task S14_selection_inspection_and_failed_spawns_do_not_change_the_revision()
    {
        await using var t = await StartAsync(("pen", "left"));
        var before = Revision(t);
        t.Engine.PlayerSelect("pen_1");
        await t.CallAsync("inspect_scene", "{}");
        await t.CallAsync("spawn_objects", SpawnJson(("sword", null, null)));
        Assert.Equal(before, Revision(t));
    }

    [Fact]
    public async Task S84_a_spawned_object_cannot_be_used_until_the_next_round()
    {
        await using var t = await StartAsync();
        await t.CallAsync("spawn_objects", SpawnJson(("cup", "left", null)));
        var r = await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "right"));
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
        Assert.Equal("left", Inst(t, "cup_1").Region);
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "right"))).IsSuccess);
    }

    [Fact]
    public async Task S24_a_repeated_call_id_spawns_once()
    {
        await using var t = await StartAsync();
        var first = await t.CallAsync("spawn_objects", SpawnJson(("cup", null, null)), "c1");
        var second = await t.CallAsync("spawn_objects", SpawnJson(("cup", null, null)), "c1");
        Assert.Single(Instances(t));
        Assert.Equal(first.AffectedIds, second.AffectedIds);
        Assert.Equal(first.Revision, second.Revision);
        Assert.Single(SceneLog(t));
    }

    [Fact]
    public async Task S22_a_failed_spawn_adds_no_event_and_raises_no_scene_notification()
    {
        await using var t = await StartAsync();
        await AssertRefusedAsync(t, "spawn_objects", SpawnJson(("sword", null, null)), ToolErrorCodes.UnknownCatalogId);
        Assert.Empty(t.Events.SceneEvents);
    }
}

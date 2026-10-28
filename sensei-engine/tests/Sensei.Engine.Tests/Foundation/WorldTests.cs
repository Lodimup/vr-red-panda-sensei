using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>Scene model, slots, revision, events and player actions (S8, S12–S20, S105, S115).</summary>
public sealed class WorldTests
{
    [Fact]
    public async Task S8_scene_starts_empty_at_revision_0()
    {
        await using var t = await TestEngine.CreateAsync();
        var scene = t.Engine.GetScene();
        Assert.Equal(0, scene.Revision);
        Assert.Empty(scene.Instances);
        Assert.Null(scene.SelectedId);
        Assert.Null(scene.ActiveTask);
        Assert.Null(t.Engine.GetActiveTask());
    }

    [Fact]
    public async Task S8_scene_is_not_persisted_across_engines()
    {
        var store = new InMemoryProfileStore();
        await using (var first = await TestEngine.CreateAsync(store: store))
        {
            Spawn(first, "apple");
        }
        await using var second = await TestEngine.CreateAsync(store: store);
        Assert.Empty(second.Engine.GetScene().Instances);
        Assert.Equal(0, second.Engine.GetScene().Revision);
        Assert.DoesNotContain("apple_1", second.Store.RawJson("default"));
    }

    [Fact]
    public async Task S12_instance_ids_count_per_catalog_id_from_1()
    {
        await using var t = await TestEngine.CreateAsync();
        var ids = Spawn(t, ("cup", "left"), ("apple", "center"), ("cup", "right"));
        Assert.Equal(new[] { "cup_1", "apple_1", "cup_2" }, ids);
    }

    [Fact]
    public async Task S12_instance_ids_are_never_reused_after_removal()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        Remove(t, a);
        Assert.Equal("apple_2", Spawn(t, "apple"));
    }

    [Fact]
    public async Task S13_sixteen_instances_fit_and_a_seventeenth_is_refused()
    {
        await using var t = await TestEngine.CreateAsync();
        for (var i = 0; i < 16; i++) Spawn(t, "pen");
        Assert.Equal(16, t.Engine.GetScene().Instances.Count);
        Assert.Throws<InvalidOperationException>(() => Spawn(t, "pen"));
        Assert.Equal(16, t.Engine.GetScene().Instances.Count);
        Assert.Equal(16, SceneWorld.Capacity);
    }

    [Fact]
    public async Task S13_instances_in_containers_and_hands_count_toward_capacity()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag");
        var key = Spawn(t, "key");
        SenseiMove(t, key, null, bag);
        var pen = Spawn(t, "pen");
        t.Engine.PlayerMove(pen, SceneRegions.LearnerHand, null);
        for (var i = 0; i < 13; i++) Spawn(t, "apple");
        Assert.Throws<InvalidOperationException>(() => Spawn(t, "apple"));
    }

    [Fact]
    public async Task S14_one_spawn_of_three_objects_is_one_revision()
    {
        await using var t = await TestEngine.CreateAsync();
        Spawn(t, ("cup", "left"), ("cup", "left"), ("cup", "left"));
        Assert.Equal(1, t.Engine.GetScene().Revision);
        Assert.Single(t.Core.World.Events);
    }

    [Fact]
    public async Task S14_moves_and_removals_add_one_revision_select_adds_none()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        SenseiMove(t, a, SceneRegions.Left, null);
        Assert.Equal(2, t.Engine.GetScene().Revision);
        t.Engine.PlayerMove(a, SceneRegions.Right, null);
        Assert.Equal(3, t.Engine.GetScene().Revision);
        t.Engine.PlayerSelect(a);
        Assert.Equal(3, t.Engine.GetScene().Revision);
        Remove(t, a);
        Assert.Equal(4, t.Engine.GetScene().Revision);
    }

    [Fact]
    public async Task S15_each_practice_region_keeps_its_own_slot_counter()
    {
        await using var t = await TestEngine.CreateAsync();
        var ids = Spawn(t, ("apple", "left"), ("banana", "left"), ("cup", "center"), ("pen", "left"));
        Assert.Equal(new[] { 1, 2, 1, 3 }, ids.Select(id => Instance(t, id).Slot));
    }

    [Fact]
    public async Task S15_an_object_entering_a_region_gets_the_next_slot()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple", SceneRegions.Left);
        var b = Spawn(t, "banana", SceneRegions.Center);
        SenseiMove(t, b, SceneRegions.Left, null);
        Assert.Equal(2, Instance(t, b).Slot);
        SenseiMove(t, a, SceneRegions.Center, null);
        Assert.Equal(2, Instance(t, a).Slot);
        SenseiMove(t, a, SceneRegions.Left, null);
        Assert.Equal(3, Instance(t, a).Slot);
    }

    [Fact]
    public async Task S15_objects_entering_containers_or_hands_keep_their_slot()
    {
        await using var t = await TestEngine.CreateAsync();
        var box = Spawn(t, "box", SceneRegions.Right);
        var a = Spawn(t, "apple", SceneRegions.Left);
        Spawn(t, "apple", SceneRegions.Left);
        var b = Spawn(t, "apple", SceneRegions.Left);
        SenseiMove(t, b, null, box);
        Assert.Equal(3, Instance(t, b).Slot);
        t.Engine.PlayerMove(a, SceneRegions.LearnerHand, null);
        Assert.Equal(1, Instance(t, a).Slot);
    }

    [Fact]
    public async Task S16_an_object_in_a_container_takes_the_container_region()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag", SceneRegions.Right);
        var key = Spawn(t, "key", SceneRegions.Left);
        SenseiMove(t, key, null, bag);
        var inst = Instance(t, key);
        Assert.Equal(bag, inst.ContainerId);
        Assert.Equal(SceneRegions.Right, inst.Region);
    }

    [Fact]
    public async Task S16_moving_a_container_moves_its_contents_region()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag", SceneRegions.Right);
        var key = Spawn(t, "key", SceneRegions.Left);
        SenseiMove(t, key, null, bag);
        var e = SenseiMove(t, bag, SceneRegions.Left, null);
        Assert.Equal(SceneRegions.Left, Instance(t, key).Region);
        Assert.Equal(bag, Instance(t, key).ContainerId);
        Assert.Equal(new[] { bag, key }, e.InstanceIds);
    }

    [Fact]
    public async Task S16_moving_out_of_a_container_to_a_region_clears_container_id()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag", SceneRegions.Right);
        var key = Spawn(t, "key", SceneRegions.Left);
        SenseiMove(t, key, null, bag);
        SenseiMove(t, key, SceneRegions.Center, null);
        Assert.Null(Instance(t, key).ContainerId);
        Assert.Equal(SceneRegions.Center, Instance(t, key).Region);
    }

    [Fact]
    public async Task S17_events_carry_every_field()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("x");
        var a = Spawn(t, "apple", SceneRegions.Left);
        SenseiMove(t, a, SceneRegions.Right, null, MovePurposes.Demonstrate);
        t.Engine.PlayerMove(a, SceneRegions.Center, null);
        var events = t.Core.World.Events;
        Assert.Equal(new long[] { 1, 2, 3 }, events.Select(e => e.EventId));
        Assert.Equal(new long[] { 1, 2, 3 }, events.Select(e => e.Revision));
        Assert.Equal(new[] { "spawned", "moved", "moved" }, events.Select(e => e.Kind));
        Assert.Equal(new[] { "sensei", "sensei", "player" }, events.Select(e => e.Actor));
        Assert.Equal("left", events[0].To);
        Assert.Equal("left", events[1].From);
        Assert.Equal("right", events[1].To);
        Assert.Equal("demonstrate", events[1].Purpose);
        Assert.Null(events[2].Purpose);
        Assert.All(events, e => Assert.Equal(1, e.TurnId));
        Assert.All(events, e => Assert.Equal(new[] { a }, e.InstanceIds));
    }

    [Fact]
    public async Task S17_moves_into_containers_name_the_container()
    {
        await using var t = await TestEngine.CreateAsync();
        var box = Spawn(t, "box");
        var pen = Spawn(t, "pen", SceneRegions.Left);
        var into = SenseiMove(t, pen, null, box);
        var outOf = SenseiMove(t, pen, SceneRegions.Right, null);
        Assert.Equal(("left", box), (into.From, into.To));
        Assert.Equal((box, "right"), (outOf.From, outOf.To));
    }

    [Fact]
    public async Task S17_scene_changes_raise_on_scene_changed_with_a_snapshot()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        Assert.Equal(new[] { "spawned", "moved" }, t.Events.SceneEvents.Select(e => e.Kind));
        Assert.Equal(new long[] { 1, 2 }, t.Events.SceneSnapshots.Select(s => s.Revision));
        Assert.Equal("left", t.Events.SceneSnapshots[1].Instances.Single().Region);
    }

    [Fact]
    public async Task S18_select_unknown_object_is_unknown_object()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = t.Engine.PlayerSelect("apple_1");
        Assert.False(r.Ok);
        Assert.Equal(ToolErrorCodes.UnknownObject, r.ErrorCode);
        Assert.Empty(t.Core.World.Events);
    }

    [Fact]
    public async Task S18_select_sets_selected_id_and_appends_a_selected_event_without_revision()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var r = t.Engine.PlayerSelect(a);
        Assert.True(r.Ok);
        Assert.Null(r.ErrorCode);
        Assert.Equal(1, r.Revision);
        Assert.Equal(a, t.Engine.GetScene().SelectedId);
        var e = t.Core.World.Events.Last();
        Assert.Equal(SceneEventKinds.Selected, e.Kind);
        Assert.Equal(SceneActors.Player, e.Actor);
        Assert.Equal(1, e.Revision);
    }

    [Fact]
    public async Task S32b_removing_the_selected_object_clears_the_selection()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        t.Engine.PlayerSelect(a);
        Remove(t, a);
        Assert.Null(t.Engine.GetScene().SelectedId);
    }

    [Theory]
    [InlineData("left", "box_1", "invalid_arguments")]
    [InlineData(null, null, "invalid_arguments")]
    [InlineData("kitchen", null, "invalid_arguments")]
    [InlineData("LEFT", null, "invalid_arguments")]
    [InlineData(null, "bag_9", "unknown_object")]
    [InlineData(null, "cup_1", "invalid_destination")]
    [InlineData(null, "apple_1", "invalid_destination")]
    [InlineData("center", null, "invalid_destination")]
    public async Task S19_player_move_destination_rules(string? region, string? container, string expected)
    {
        await using var t = await TestEngine.CreateAsync();
        Spawn(t, ("apple", "center"), ("box", "left"), ("cup", "right"));
        var r = t.Engine.PlayerMove("apple_1", region, container);
        Assert.False(r.Ok);
        Assert.Equal(expected, r.ErrorCode);
        Assert.Equal(1, r.Revision);
        Assert.Single(t.Core.World.Events);
    }

    [Fact]
    public async Task S19_player_move_of_unknown_object_is_unknown_object()
    {
        await using var t = await TestEngine.CreateAsync();
        Assert.Equal(ToolErrorCodes.UnknownObject, t.Engine.PlayerMove("ghost_1", SceneRegions.Left, null).ErrorCode);
    }

    [Fact]
    public async Task S19_a_container_cannot_go_into_a_container()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag");
        var box = Spawn(t, "box");
        Assert.Equal(ToolErrorCodes.InvalidDestination, t.Engine.PlayerMove(bag, null, box).ErrorCode);
    }

    [Fact]
    public async Task S19_an_object_already_in_that_container_is_invalid_destination()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag");
        var key = Spawn(t, "key");
        Assert.True(t.Engine.PlayerMove(key, null, bag).Ok);
        Assert.Equal(ToolErrorCodes.InvalidDestination, t.Engine.PlayerMove(key, null, bag).ErrorCode);
    }

    [Fact]
    public async Task S19_an_object_in_a_container_can_move_to_the_container_region()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag", SceneRegions.Left);
        var key = Spawn(t, "key", SceneRegions.Left);
        t.Engine.PlayerMove(key, null, bag);
        Assert.True(t.Engine.PlayerMove(key, SceneRegions.Left, null).Ok);
    }

    [Fact]
    public async Task S19_successful_player_move_commits_as_player_and_returns_the_revision()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var r = t.Engine.PlayerMove(a, SceneRegions.LearnerHand, null);
        Assert.True(r.Ok);
        Assert.Null(r.ErrorCode);
        Assert.Equal(2, r.Revision);
        Assert.Equal(SceneActors.Player, t.Core.World.Events.Last().Actor);
        Assert.Equal(SceneRegions.LearnerHand, t.Engine.GetScene().Instances.Single().Region);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    [InlineData("learner_hand")]
    [InlineData("sensei_side")]
    public async Task S115_every_named_region_is_a_valid_destination(string region)
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple", SceneRegions.SenseiSide == region ? SceneRegions.Center : SceneRegions.SenseiSide);
        Assert.True(t.Engine.PlayerMove(a, region, null).Ok);
    }

    [Fact]
    public async Task S20_snapshot_carries_names_support_and_active_task()
    {
        await using var t = await TestEngine.CreateAsync();
        var cup = Spawn(t, "cup", SceneRegions.Left);
        var s = t.Engine.GetScene();
        var inst = s.Instances.Single();
        Assert.Equal((cup, "cup", "left", 1), (inst.Id, inst.CatalogId, inst.Region, inst.Slot));
        Assert.Null(inst.ContainerId);
        Assert.Equal(("コップ", "コップ", "cup"), (inst.Japanese, inst.Reading, inst.English));
        Assert.True(s.Support.ShowRomaji);
        Assert.Equal("normal", s.Support.Pace);
    }

    [Fact]
    public async Task S20_snapshots_are_copies()
    {
        await using var t = await TestEngine.CreateAsync();
        Spawn(t, "apple");
        var s = t.Engine.GetScene();
        s.Instances[0].Region = "right";
        s.Support.ShowRomaji = false;
        Assert.Equal("center", t.Engine.GetScene().Instances[0].Region);
        Assert.True(t.Engine.GetScene().Support.ShowRomaji);
    }

    [Fact]
    public async Task S9_player_actions_from_many_threads_serialize()
    {
        await using var t = await TestEngine.CreateAsync();
        var ids = Enumerable.Range(0, 8).Select(_ => Spawn(t, "pen")).ToList();
        var regions = new[] { SceneRegions.Left, SceneRegions.Right };
        var tasks = Enumerable.Range(0, 400).Select(i => Task.Run(() =>
            i % 5 == 0 ? t.Engine.PlayerSelect(ids[i % 8]) : t.Engine.PlayerMove(ids[i % 8], regions[(i / 8) % 2], null))).ToArray();
        var results = await Task.WhenAll(tasks);
        var moves = results.Where((r, i) => i % 5 != 0 && r.Ok).Count();
        Assert.Equal(8 + moves, t.Engine.GetScene().Revision);
        Assert.Equal(Enumerable.Range(1, t.Core.World.Events.Count).Select(x => (long)x), t.Core.World.Events.Select(e => e.EventId));
    }
}

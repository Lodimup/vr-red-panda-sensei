using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.World;

/// <summary>move_object (S31a–d, S115) and remove_objects (S32a–b, S83, S107).</summary>
public sealed class MoveRemoveTests
{
    // ------------------------------------------------------------------ S31a exactly one destination

    [Fact]
    public async Task S31a_both_region_and_container_is_invalid_arguments()
    {
        await using var t = await StartAsync(("cup", "left"), ("bag", "right"));
        var json = """{"object_id":"cup_1","destination":{"region":"center","container_id":"bag_1"},"purpose":"arrange"}""";
        await AssertRefusedAsync(t, "move_object", json, ToolErrorCodes.InvalidArguments, "exactly one of region or container_id");
    }

    [Fact]
    public async Task S31a_neither_region_nor_container_is_invalid_arguments()
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "move_object", MoveJson("cup_1", "arrange"), ToolErrorCodes.InvalidArguments, "exactly one of region or container_id");
    }

    [Fact]
    public async Task S114_a_null_destination_field_counts_as_omitted()
    {
        await using var t = await StartAsync(("cup", "left"), ("bag", "right"));
        var json = """{"object_id":"cup_1","destination":{"region":null,"container_id":"bag_1"},"purpose":"arrange"}""";
        var r = await t.CallAsync("move_object", json);
        Assert.True(r.IsSuccess);
        Assert.Equal("bag_1", Inst(t, "cup_1").ContainerId);
    }

    [Fact]
    public async Task S31a_unknown_object_is_unknown_object()
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "move_object", MoveJson("pen_1", "arrange", region: "right"), ToolErrorCodes.UnknownObject, "pen_1");
    }

    [Fact]
    public async Task S31a_unknown_container_is_unknown_object()
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "move_object", MoveJson("cup_1", "arrange", container: "bag_1"), ToolErrorCodes.UnknownObject, "bag_1");
    }

    [Fact]
    public async Task S31a_an_id_of_a_removed_object_is_unknown()
    {
        await using var t = await StartAsync(("cup", "left"));
        await t.CallAsync("remove_objects", Ids("cup_1"));
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "move_object", MoveJson("cup_1", "arrange", region: "right"), ToolErrorCodes.UnknownObject);
    }

    [Theory]
    [InlineData("sky")]
    [InlineData("Left")]
    [InlineData("")]
    public async Task S115_a_region_name_outside_the_five_is_invalid_arguments(string region)
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "move_object", MoveJson("cup_1", "arrange", region: region), ToolErrorCodes.InvalidArguments, "region");
    }

    [Theory]
    [InlineData("pick up")]
    [InlineData("Arrange")]
    public async Task S31_purpose_must_be_one_of_the_three(string purpose)
    {
        await using var t = await StartAsync(("cup", "left"));
        var r = await AssertRefusedAsync(t, "move_object", MoveJson("cup_1", purpose, region: "right"), ToolErrorCodes.InvalidArguments, "purpose");
        Assert.Contains("fulfill_request", r.Detail);
    }

    [Theory]
    [InlineData("""{"destination":{"region":"left"},"purpose":"arrange"}""")]
    [InlineData("""{"object_id":"cup_1","purpose":"arrange"}""")]
    [InlineData("""{"object_id":"cup_1","destination":{"region":"left"}}""")]
    [InlineData("""{"object_id":"cup_1","destination":{"region":"left","slot":2},"purpose":"arrange"}""")]
    [InlineData("""{"object_id":"cup_1","destination":{"region":"left"},"purpose":"arrange","speed":1}""")]
    [InlineData("""{"object_id":3,"destination":{"region":"left"},"purpose":"arrange"}""")]
    public async Task S21_move_object_arguments_are_strict(string json)
    {
        await using var t = await StartAsync(("cup", "right"));
        await AssertRefusedAsync(t, "move_object", json, ToolErrorCodes.InvalidArguments);
    }

    // ------------------------------------------------------------------ S31b invalid_destination

    [Fact]
    public async Task S31b_container_into_a_non_container_is_invalid_destination()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        await AssertRefusedAsync(t, "move_object", MoveJson("cup_1", "arrange", container: "pen_1"), ToolErrorCodes.InvalidDestination, "not a container");
    }

    [Fact]
    public async Task S31b_container_into_container_is_invalid_destination()
    {
        await using var t = await StartAsync(("bag", "left"), ("box", "right"));
        await AssertRefusedAsync(t, "move_object", MoveJson("bag_1", "arrange", container: "box_1"), ToolErrorCodes.InvalidDestination);
    }

    [Fact]
    public async Task S31b_an_object_cannot_move_into_itself()
    {
        await using var t = await StartAsync(("bag", "left"));
        await AssertRefusedAsync(t, "move_object", MoveJson("bag_1", "arrange", container: "bag_1"), ToolErrorCodes.InvalidDestination);
    }

    [Fact]
    public async Task S31b_moving_to_the_region_it_is_already_in_is_invalid_destination()
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "move_object", MoveJson("cup_1", "arrange", region: "left"), ToolErrorCodes.InvalidDestination, "already in left");
    }

    [Fact]
    public async Task S31b_moving_into_the_container_it_is_already_in_is_invalid_destination()
    {
        await using var t = await StartAsync(("cup", "left"), ("bag", "right"));
        Assert.True((await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"))).IsSuccess);
        await AssertRefusedAsync(t, "move_object", MoveJson("cup_1", "arrange", container: "bag_1"), ToolErrorCodes.InvalidDestination, "already in");
    }

    [Fact]
    public async Task S31b_an_object_inside_a_container_may_go_to_that_containers_region()
    {
        await using var t = await StartAsync(("cup", "left"), ("bag", "right"));
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"));
        var r = await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "right"));
        Assert.True(r.IsSuccess);
        Assert.Null(Inst(t, "cup_1").ContainerId);
        Assert.Equal("right", Inst(t, "cup_1").Region);
    }

    [Fact]
    public async Task S31b_a_container_may_move_between_regions_and_a_plain_object_into_either_container()
    {
        await using var t = await StartAsync(("bag", "left"), ("box", "right"), ("cup", "center"), ("pen", "center"));
        Assert.True((await t.CallAsync("move_object", MoveJson("bag_1", "arrange", region: "center"))).IsSuccess);
        Assert.True((await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"))).IsSuccess);
        Assert.True((await t.CallAsync("move_object", MoveJson("pen_1", "arrange", container: "box_1"))).IsSuccess);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    [InlineData("learner_hand")]
    [InlineData("sensei_side")]
    public async Task S31_every_region_is_a_valid_destination(string region)
    {
        // the cup starts in a region different from each of the five targets
        await using var t = await StartAsync(("cup", region == "left" ? "right" : "left"));
        var r = await t.CallAsync("move_object", MoveJson("cup_1", "fulfill_request", region: region));
        Assert.True(r.IsSuccess);
        Assert.Equal(region, Inst(t, "cup_1").Region);
    }

    // ------------------------------------------------------------------ S31c success

    [Fact]
    public async Task S31c_a_move_is_one_moved_event_with_from_to_and_purpose_and_one_revision()
    {
        await using var t = await StartAsync(("cup", "left"));
        var before = Revision(t);
        var r = await t.CallAsync("move_object", MoveJson("cup_1", "fulfill_request", region: "right"));
        Assert.True(r.IsSuccess);
        Assert.Equal(new[] { "cup_1" }, r.AffectedIds);
        Assert.Equal(before + 1, Revision(t));
        var e = SceneLog(t).Last();
        Assert.Equal("moved", e.Kind);
        Assert.Equal("sensei", e.Actor);
        Assert.Equal("left", e.From);
        Assert.Equal("right", e.To);
        Assert.Equal("fulfill_request", e.Purpose);
        Assert.Equal(before + 1, e.Revision);
        Assert.Equal(new[] { "cup_1" }, e.InstanceIds);
        Assert.Equal(e.EventId, t.Events.SceneEvents.Last().EventId);
    }

    [Fact]
    public async Task S31c_moving_into_a_container_sets_container_id_and_the_containers_region()
    {
        await using var t = await StartAsync(("cup", "left"), ("bag", "right"));
        var r = await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"));
        Assert.True(r.IsSuccess);
        var cup = Inst(t, "cup_1");
        Assert.Equal("bag_1", cup.ContainerId);
        Assert.Equal("right", cup.Region);
        var e = SceneLog(t).Last();
        Assert.Equal("left", e.From);
        Assert.Equal("bag_1", e.To);
    }

    [Fact]
    public async Task S31c_moving_to_a_region_clears_container_id_and_records_the_container_as_from()
    {
        await using var t = await StartAsync(("cup", "left"), ("bag", "right"));
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"));
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "center"));
        var cup = Inst(t, "cup_1");
        Assert.Null(cup.ContainerId);
        Assert.Equal("center", cup.Region);
        Assert.Equal("bag_1", SceneLog(t).Last().From);
        Assert.Equal("center", SceneLog(t).Last().To);
    }

    [Fact]
    public async Task S31c_moving_a_container_lists_its_contents_as_affected_and_moves_their_region()
    {
        await using var t = await StartAsync(("bag", "left"), ("cup", "left"), ("pen", "left"), ("apple", "right"));
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"));
        await t.CallAsync("move_object", MoveJson("pen_1", "arrange", container: "bag_1"));
        var r = await t.CallAsync("move_object", MoveJson("bag_1", "arrange", region: "center"));
        Assert.True(r.IsSuccess);
        Assert.Equal(new[] { "bag_1", "cup_1", "pen_1" }, r.AffectedIds);
        Assert.Equal("center", Inst(t, "cup_1").Region);
        Assert.Equal("bag_1", Inst(t, "cup_1").ContainerId);
        Assert.Equal("center", Inst(t, "pen_1").Region);
        Assert.Equal("right", Inst(t, "apple_1").Region);
        Assert.Equal(new[] { "bag_1", "cup_1", "pen_1" }, SceneLog(t).Last().InstanceIds);
    }

    [Fact]
    public async Task S31c_the_slot_is_the_next_value_of_the_destination_region()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"), ("book", "right"));
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "right"));
        Assert.Equal(3, Inst(t, "cup_1").Slot);
    }

    [Fact]
    public async Task S31c_a_move_does_not_change_the_revision_when_refused_and_is_stale_checked()
    {
        await using var t = await StartAsync(("cup", "left"));
        Assert.True(t.Engine.PlayerMove("cup_1", "right", null).Ok);
        var before = Revision(t);
        var r = await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "center"));
        Assert.Equal(ToolErrorCodes.StaleScene, r.ErrorCode);
        Assert.Equal(before, Revision(t));
        Assert.Equal("right", Inst(t, "cup_1").Region);
    }

    // ------------------------------------------------------------------ S31d demonstrate

    [Fact]
    public async Task S31d_demonstrate_records_demonstration_for_every_skill_target_of_the_active_task()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "center")), targets: "[" + ListenTarget + "," + ProductionTarget + "]");
        t.Engine.MarkRoundStart();
        var r = await t.CallAsync("move_object", MoveJson("pen_1", "demonstrate", region: "left"));
        Assert.True(r.IsSuccess);
        var aid = Aid(t);
        Assert.Equal(2, aid.Count);
        Assert.All(aid, a => Assert.Equal(AssistanceKinds.Demonstration, a.Kind));
        Assert.Equal(new[] { "ringo", "onegai" }, aid.Select(a => a.TargetKey));
        Assert.All(aid, a => Assert.Equal("t1", a.TaskId));
    }

    [Theory]
    [InlineData("fulfill_request")]
    [InlineData("arrange")]
    public async Task S31d_other_purposes_record_no_assistance(string purpose)
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "center")));
        t.Engine.MarkRoundStart();
        await t.CallAsync("move_object", MoveJson("pen_1", purpose, region: "left"));
        Assert.Empty(Aid(t));
    }

    [Fact]
    public async Task S31d_demonstrate_without_an_active_task_records_nothing()
    {
        await using var t = await StartAsync(("pen", "right"));
        var r = await t.CallAsync("move_object", MoveJson("pen_1", "demonstrate", region: "left"));
        Assert.True(r.IsSuccess);
        Assert.Empty(Aid(t));
    }

    [Fact]
    public async Task S31d_a_refused_demonstration_records_nothing()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "center")));
        t.Engine.MarkRoundStart();
        await AssertRefusedAsync(t, "move_object", MoveJson("pen_1", "demonstrate", region: "right"), ToolErrorCodes.InvalidDestination);
        Assert.Empty(Aid(t));
    }

    [Fact]
    public async Task S31d_a_demonstration_that_completes_the_task_is_still_recorded()
    {
        await using var t = await StartAsync(("cup", "left"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        t.Engine.MarkRoundStart();
        var r = await t.CallAsync("move_object", MoveJson("cup_1", "demonstrate", region: "right"));
        Assert.True(r.IsSuccess);
        Assert.Equal("completed", Data(r).GetProperty("task_status").GetString());
        var a = Assert.Single(Aid(t));
        Assert.Equal(AssistanceKinds.Demonstration, a.Kind);
        Assert.Equal("ringo", a.TargetKey);
        Assert.Null(t.Engine.GetActiveTask());
    }

    // ------------------------------------------------------------------ S32a remove_objects refusals

    [Fact]
    public async Task S32a_unknown_id_is_unknown_object_and_nothing_is_removed()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        await AssertRefusedAsync(t, "remove_objects", Ids("cup_1", "book_1"), ToolErrorCodes.UnknownObject, "object_ids[1]");
        Assert.Equal(2, Instances(t).Count);
    }

    [Theory]
    [InlineData("object_id")]
    [InlineData("reference_id")]
    [InlineData("container_id")]
    public async Task S32a_an_id_referenced_by_an_active_task_predicate_cannot_be_removed(string field)
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"), ("bag", "center"), ("book", "center"));
        var predicate = field switch
        {
            "object_id" => Obj("cup_1", "right"),
            "reference_id" => """{"type":"right_of","object_id":"cup_1","reference_id":"pen_1"}""",
            _ => """{"type":"contained_in","object_id":"cup_1","container_id":"bag_1"}""",
        };
        await StartTaskAsync(t, Predicates(predicate));
        t.Engine.MarkRoundStart();
        var target = field switch { "object_id" => "cup_1", "reference_id" => "pen_1", _ => "bag_1" };
        await AssertRefusedAsync(t, "remove_objects", Ids(target), ToolErrorCodes.ObjectInActiveTask, target);
        t.Engine.BeginTurn("n");
        var unrelated = await t.CallAsync("remove_objects", Ids("book_1"));
        Assert.True(unrelated.IsSuccess);
    }

    [Fact]
    public async Task S32a_the_task_check_covers_the_whole_batch()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"), ("book", "center"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        t.Engine.MarkRoundStart();
        await AssertRefusedAsync(t, "remove_objects", Ids("book_1", "cup_1"), ToolErrorCodes.ObjectInActiveTask);
        Assert.Equal(3, Instances(t).Count);
    }

    [Fact]
    public async Task S32a_unknown_object_is_reported_before_object_in_active_task()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        t.Engine.MarkRoundStart();
        await AssertRefusedAsync(t, "remove_objects", Ids("cup_1", "book_1"), ToolErrorCodes.UnknownObject);
    }

    [Fact]
    public async Task S32a_a_finished_task_no_longer_protects_its_objects()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        var start = await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        t.Engine.MarkRoundStart();
        var task = Data(start).GetProperty("task_id").GetString()!;
        Assert.True((await t.CallAsync("update_challenge", $$"""{"task_id":"{{task}}","action":"abandon","reason":"changed my mind"}""")).IsSuccess);
        Assert.True((await t.CallAsync("remove_objects", Ids("cup_1"))).IsSuccess);
    }

    [Fact]
    public async Task S32a_a_container_whose_contents_are_not_all_listed_is_container_not_empty()
    {
        await using var t = await StartAsync(("bag", "left"), ("cup", "left"), ("pen", "left"));
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"));
        await t.CallAsync("move_object", MoveJson("pen_1", "arrange", container: "bag_1"));
        t.Engine.BeginTurn("remove");
        var r = await AssertRefusedAsync(t, "remove_objects", Ids("bag_1", "cup_1"), ToolErrorCodes.InvalidArguments);
        Assert.Equal("container_not_empty", r.Detail);
        t.Engine.BeginTurn("remove");
        await AssertRefusedAsync(t, "remove_objects", Ids("bag_1"), ToolErrorCodes.InvalidArguments, "container_not_empty");
    }

    [Fact]
    public async Task S32a_a_container_goes_when_everything_inside_is_listed_in_any_order()
    {
        await using var t = await StartAsync(("bag", "left"), ("cup", "left"), ("pen", "left"));
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"));
        await t.CallAsync("move_object", MoveJson("pen_1", "arrange", container: "bag_1"));
        t.Engine.BeginTurn("remove");
        var r = await t.CallAsync("remove_objects", Ids("cup_1", "bag_1", "pen_1"));
        Assert.True(r.IsSuccess);
        Assert.Empty(Instances(t));
    }

    [Fact]
    public async Task S32a_an_empty_container_can_be_removed_alone()
    {
        await using var t = await StartAsync(("bag", "left"), ("cup", "left"));
        Assert.True((await t.CallAsync("remove_objects", Ids("bag_1"))).IsSuccess);
        Assert.Equal(new[] { "cup_1" }, Instances(t).Select(i => i.Id));
    }

    [Fact]
    public async Task S32a_a_task_reference_is_reported_before_container_not_empty()
    {
        await using var t = await StartAsync(("bag", "left"), ("cup", "left"), ("pen", "right"));
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"));
        await StartTaskAsync(t, Predicates(Obj("bag_1", "right")));
        t.Engine.MarkRoundStart();
        await AssertRefusedAsync(t, "remove_objects", Ids("bag_1"), ToolErrorCodes.ObjectInActiveTask);
    }

    // ------------------------------------------------------------------ S107

    [Fact]
    public async Task S107_removal_that_leaves_too_few_selector_matches_for_an_eq_quantity_is_refused()
    {
        await using var t = await StartAsync(("apple", "left"), ("apple", "left"), ("apple", "center"), ("pen", "right"));
        var quantity = """{"type":"quantity","selector":{"catalog_id":"apple"},"region":"right","count":3,"comparison":"gte"}""";
        await StartTaskAsync(t, Predicates(quantity, Obj("pen_1", "left")));
        t.Engine.MarkRoundStart();
        await AssertRefusedAsync(t, "remove_objects", Ids("apple_3"), ToolErrorCodes.ObjectInActiveTask, "needs 3");
    }

    [Fact]
    public async Task S107_removal_that_still_leaves_enough_matches_is_allowed()
    {
        await using var t = await StartAsync(("apple", "left"), ("apple", "left"), ("apple", "center"), ("pen", "right"));
        var quantity = """{"type":"quantity","selector":{"catalog_id":"apple"},"region":"right","count":2,"comparison":"gte"}""";
        await StartTaskAsync(t, Predicates(quantity));
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("remove_objects", Ids("apple_3"))).IsSuccess);
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "remove_objects", Ids("apple_2"), ToolErrorCodes.ObjectInActiveTask);
    }

    [Fact]
    public async Task S107_a_lte_quantity_never_blocks_removal()
    {
        await using var t = await StartAsync(("apple", "left"), ("apple", "left"), ("pen", "right"));
        var quantity = """{"type":"quantity","selector":{"catalog_id":"apple"},"region":"right","count":2,"comparison":"lte"}""";
        await StartTaskAsync(t, Predicates(quantity, Obj("pen_1", "left")));
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("remove_objects", Ids("apple_1", "apple_2"))).IsSuccess);
    }

    [Fact]
    public async Task S107_removing_the_last_match_of_an_in_region_selector_is_refused()
    {
        await using var t = await StartAsync(("cup", "left"), ("cup", "right"), ("pen", "right"));
        await StartTaskAsync(t, Predicates("""{"type":"in_region","selector":{"catalog_id":"cup"},"region":"center"}"""));
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("remove_objects", Ids("cup_2"))).IsSuccess);
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "remove_objects", Ids("cup_1"), ToolErrorCodes.ObjectInActiveTask);
    }

    [Fact]
    public async Task S107_an_object_named_in_scenario_properties_cannot_be_removed()
    {
        await using var t = await StartAsync(("cup", "left"), ("cup", "right"), ("pen", "right"));
        var predicates = Predicates("""{"type":"in_region","selector":{"catalog_id":"cup","property":{"name":"content","value":"hot"}},"region":"center"}""");
        await StartTaskAsync(t, predicates, extra: "\"scenario_properties\":[{\"object_id\":\"cup_1\",\"name\":\"content\",\"value\":\"hot\"}]");
        t.Engine.MarkRoundStart();
        await AssertRefusedAsync(t, "remove_objects", Ids("cup_1"), ToolErrorCodes.ObjectInActiveTask, "scenario property");
    }

    // ------------------------------------------------------------------ S32b success

    [Fact]
    public async Task S32b_success_is_one_removed_event_one_revision_and_lists_the_ids()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"), ("book", "center"));
        var before = Revision(t);
        var r = await t.CallAsync("remove_objects", Ids("pen_1", "cup_1"));
        Assert.True(r.IsSuccess);
        Assert.Equal(new[] { "pen_1", "cup_1" }, r.AffectedIds);
        Assert.Equal(before + 1, Revision(t));
        var e = SceneLog(t).Last();
        Assert.Equal("removed", e.Kind);
        Assert.Equal("sensei", e.Actor);
        Assert.Equal(new[] { "pen_1", "cup_1" }, e.InstanceIds);
        Assert.Equal(new[] { "book_1" }, Instances(t).Select(i => i.Id));
        Assert.Equal(e.EventId, t.Events.SceneEvents.Last().EventId);
    }

    [Fact]
    public async Task S32b_removing_the_selected_object_clears_the_selection()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        t.Engine.PlayerSelect("cup_1");
        Assert.Equal("cup_1", t.Engine.GetScene().SelectedId);
        t.Engine.BeginTurn("n");
        await t.CallAsync("remove_objects", Ids("cup_1"));
        Assert.Null(t.Engine.GetScene().SelectedId);
    }

    [Fact]
    public async Task S32b_removing_another_object_keeps_the_selection()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        t.Engine.PlayerSelect("cup_1");
        t.Engine.BeginTurn("n");
        await t.CallAsync("remove_objects", Ids("pen_1"));
        Assert.Equal("cup_1", t.Engine.GetScene().SelectedId);
    }

    // ------------------------------------------------------------------ bounds and S83

    [Fact]
    public async Task S32_one_to_sixteen_ids_are_accepted_and_zero_or_seventeen_are_not()
    {
        await using var t = await StartAsync();
        await t.CallAsync("spawn_objects", SpawnJson(("apple", "left", 4), ("pen", "left", 2)));
        await t.CallAsync("spawn_objects", SpawnJson(("cup", "center", 4), ("book", "center", 2)));
        await t.CallAsync("spawn_objects", SpawnJson(("key", "right", 3)));
        Assert.Equal(15, Instances(t).Count);
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "remove_objects", """{"object_ids":[]}""", ToolErrorCodes.InvalidArguments, "object_ids");
        var sixteen = Instances(t).Select(i => i.Id).Append("water_1").ToArray();
        Assert.Equal(16, sixteen.Length);
        await AssertRefusedAsync(t, "remove_objects", Ids(sixteen.Append("water_2").ToArray()), ToolErrorCodes.InvalidArguments, "object_ids");
        t.Engine.BeginTurn("n");
        // sixteen ids with one unknown is accepted by the bounds and refused as an unknown object
        await AssertRefusedAsync(t, "remove_objects", Ids(sixteen), ToolErrorCodes.UnknownObject, "water_1");
        t.Engine.BeginTurn("n");
        var all = Instances(t).Select(i => i.Id).ToArray();
        var r = await t.CallAsync("remove_objects", Ids(all));
        Assert.True(r.IsSuccess);
        Assert.Equal(15, r.AffectedIds.Count);
        Assert.Empty(Instances(t));
    }

    [Fact]
    public async Task S83_duplicate_ids_in_remove_objects_are_invalid_and_nothing_is_removed()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        await AssertRefusedAsync(t, "remove_objects", Ids("pen_1", "cup_1", "pen_1"), ToolErrorCodes.InvalidArguments, "duplicate_id");
        Assert.Equal(2, Instances(t).Count);
    }

    [Fact]
    public async Task S83_duplicate_property_names_in_move_arguments_are_invalid()
    {
        await using var t = await StartAsync(("cup", "left"));
        var json = """{"object_id":"cup_1","object_id":"cup_1","destination":{"region":"right"},"purpose":"arrange"}""";
        await AssertRefusedAsync(t, "move_object", json, ToolErrorCodes.InvalidArguments);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"object_ids":"cup_1"}""")]
    [InlineData("""{"object_ids":[1]}""")]
    [InlineData("""{"object_ids":["cup_1"],"force":true}""")]
    public async Task S21_remove_objects_arguments_are_strict(string json)
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "remove_objects", json, ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task S84_an_object_spawned_in_this_round_cannot_be_removed_until_the_next()
    {
        await using var t = await StartAsync();
        await t.CallAsync("spawn_objects", SpawnJson(("cup", null, null)));
        var r = await t.CallAsync("remove_objects", Ids("cup_1"));
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
        Assert.Single(Instances(t));
    }
}

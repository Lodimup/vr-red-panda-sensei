using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.World;

/// <summary>Gaps found by mutation testing of the world tools: S84 on every id-carrying argument, batch order, schema bounds.</summary>
public sealed class ReviewGapTests
{
    // ------------------------------------------------------------------ S84 on every referenced id

    [Fact]
    public async Task S84_a_move_into_a_container_spawned_in_the_same_round_is_id_not_yet_visible()
    {
        await using var t = await StartAsync(("cup", "left"));
        await t.CallAsync("spawn_objects", SpawnJson(("bag", "right", null)));
        var r = await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"));
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
        Assert.Null(Inst(t, "cup_1").ContainerId);
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"))).IsSuccess);
    }

    [Fact]
    public async Task S84_an_animate_target_spawned_in_the_same_round_is_id_not_yet_visible()
    {
        await using var t = await StartAsync();
        await t.CallAsync("spawn_objects", SpawnJson(("cup", null, null)));
        var r = await t.CallAsync("animate_sensei", """{"animation":"point","target_id":"cup_1"}""");
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
        Assert.DoesNotContain("OnAnimate", t.Events.Methods);
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("animate_sensei", """{"animation":"point","target_id":"cup_1"}""")).IsSuccess);
    }

    [Fact]
    public async Task S84_a_scenario_property_object_spawned_in_the_same_round_is_id_not_yet_visible()
    {
        await using var t = await StartAsync(("apple", "left"));
        await t.CallAsync("spawn_objects", SpawnJson(("cup", null, null)));
        var extra = "\"scenario_properties\":[{\"object_id\":\"cup_1\",\"name\":\"content\",\"value\":\"hot\"}]";
        var r = await t.CallAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right")), extra: extra));
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
        Assert.Null(t.Engine.GetActiveTask());
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right")), extra: extra))).IsSuccess);
    }

    // ------------------------------------------------------------------ remove_objects: every listed container is checked

    [Fact]
    public async Task S32a_a_non_empty_container_later_in_the_list_is_container_not_empty()
    {
        await using var t = await StartAsync(("pen", "left"), ("bag", "left"), ("cup", "left"), ("box", "right"), ("book", "right"));
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", container: "bag_1"));
        await t.CallAsync("move_object", MoveJson("book_1", "arrange", container: "box_1"));
        t.Engine.BeginTurn("remove");
        // box_1 is empty-checked after pen_1 and bag_1 (whose cup is listed): only the third entry is not removable
        var r = await AssertRefusedAsync(t, "remove_objects", Ids("pen_1", "bag_1", "cup_1", "box_1"), ToolErrorCodes.InvalidArguments);
        Assert.Equal("container_not_empty", r.Detail);
    }

    // ------------------------------------------------------------------ update_challenge schema and commit

    [Fact]
    public async Task S36_revise_commits_every_given_predicate_and_allows_at_most_three()
    {
        await using var t = await StartAsync(("apple", "left"), ("cup", "center"), ("pen", "right"), ("bag", "center"));
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var four = Predicates(Obj("apple_1", "center"), Obj("cup_1", "left"), Obj("pen_1", "center"), Obj("bag_1", "left"));
        await AssertRefusedAsync(t, "update_challenge", """{"task_id":"t1","action":"revise","reason":"x","predicates":""" + four + "}", ToolErrorCodes.InvalidArguments, "predicates");
        t.Engine.BeginTurn("n");
        var three = Predicates(Obj("apple_1", "center"), Obj("cup_1", "left"), Obj("pen_1", "center"));
        var r = await t.CallAsync("update_challenge", """{"task_id":"t1","action":"revise","reason":"x","predicates":""" + three + "}");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal(3, Data(r).GetProperty("predicates").GetArrayLength());
        Assert.Equal(new[] { "apple_1", "cup_1", "pen_1" }, t.Engine.GetActiveTask()!.Predicates.Select(p => p.ObjectId));
    }

    [Theory]
    [InlineData("japanese", 80, true)]
    [InlineData("japanese", 81, false)]
    [InlineData("english", 200, true)]
    [InlineData("english", 201, false)]
    public async Task S36_a_revised_instruction_has_the_same_length_limits_as_start(string language, int length, bool accepted)
    {
        await using var t = await StartAsync(("apple", "left"));
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var text = new string(language == "japanese" ? 'あ' : 'a', length);
        var json = "{\"task_id\":\"t1\",\"action\":\"revise\",\"reason\":\"x\",\"instruction\":{\"" + language + "\":\"" + text + "\"}}";
        if (accepted) Assert.True((await t.CallAsync("update_challenge", json)).IsSuccess);
        else await AssertRefusedAsync(t, "update_challenge", json, ToolErrorCodes.InvalidArguments, "instruction." + language);
    }

    // ------------------------------------------------------------------ start_challenge schema

    [Theory]
    [InlineData("""{"type":"in_region","selector":{},"region":"left"}""", "selector")]
    [InlineData("""{"type":"in_region","selector":{"property":{"name":"a","value":"b"}},"region":"left"}""", "catalog_id")]
    public async Task S21_a_selector_needs_a_catalog_id(string predicate, string path)
    {
        await using var t = await StartAsync(("cup", "center"));
        await AssertRefusedAsync(t, "start_challenge", StartJson("[" + predicate + "]"), ToolErrorCodes.InvalidArguments, path);
    }

    [Fact]
    public async Task S21_a_skill_target_needs_a_key_and_a_dimension()
    {
        await using var t = await StartAsync(("cup", "center"));
        await AssertRefusedAsync(t, "start_challenge", StartJson(Predicates(Obj("cup_1", "left")), targets: """[{"dimension":"vocabulary"}]"""), ToolErrorCodes.InvalidArguments, "key");
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "start_challenge", StartJson(Predicates(Obj("cup_1", "left")), targets: """[{"key":"k"}]"""), ToolErrorCodes.InvalidArguments, "dimension");
    }

    [Fact]
    public async Task S21_a_scenario_property_needs_object_id_name_and_value()
    {
        await using var t = await StartAsync(("cup", "center"));
        foreach (var (item, path) in new[]
                 {
                     ("{\"name\":\"state\",\"value\":\"new\"}", "object_id"),
                     ("{\"object_id\":\"cup_1\",\"value\":\"new\"}", "name"),
                     ("{\"object_id\":\"cup_1\",\"name\":\"state\"}", "value"),
                 })
        {
            t.Engine.BeginTurn("n");
            await AssertRefusedAsync(t, "start_challenge", StartJson(Predicates(Obj("cup_1", "left")), extra: "\"scenario_properties\":[" + item + "]"), ToolErrorCodes.InvalidArguments, path);
        }
    }

    [Theory]
    [InlineData(24, true)]
    [InlineData(25, false)]
    public async Task S35_scenario_property_names_are_at_most_24_characters(int length, bool accepted)
    {
        await using var t = await StartAsync(("cup", "center"));
        var extra = "\"scenario_properties\":[{\"object_id\":\"cup_1\",\"name\":\"" + new string('n', length) + "\",\"value\":\"x\"}]";
        var json = StartJson(Predicates(Obj("cup_1", "left")), extra: extra);
        if (accepted) Assert.True((await t.CallAsync("start_challenge", json)).IsSuccess);
        else await AssertRefusedAsync(t, "start_challenge", json, ToolErrorCodes.InvalidArguments, "scenario_properties[0].name");
    }

    [Theory]
    [InlineData("vocabulary")]
    [InlineData("listening")]
    [InlineData("production")]
    [InlineData("reading")]
    [InlineData("interaction")]
    [InlineData("register")]
    public async Task S35_every_skill_dimension_is_accepted(string dimension)
    {
        await using var t = await StartAsync(("cup", "center"));
        var targets = "[{\"dimension\":\"" + dimension + "\",\"key\":\"k\"}]";
        Assert.True((await t.CallAsync("start_challenge", StartJson(Predicates(Obj("cup_1", "left")), targets: targets))).IsSuccess);
    }

    [Theory]
    [InlineData("discover_request")]
    [InlineData("listen_arrange")]
    [InlineData("converse_roleplay")]
    public async Task S35_every_task_family_is_accepted(string family)
    {
        await using var t = await StartAsync(("cup", "center"));
        Assert.True((await t.CallAsync("start_challenge", StartJson(Predicates(Obj("cup_1", "left")), family: family))).IsSuccess);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    [InlineData("learner_hand")]
    [InlineData("sensei_side")]
    public async Task S35_every_region_is_accepted_in_a_predicate(string region)
    {
        await using var t = await StartAsync(("cup", "learner_hand"));
        var destination = region == "learner_hand" ? "left" : region;
        Assert.True((await t.CallAsync("start_challenge", StartJson(Predicates(Obj("cup_1", destination))))).IsSuccess);
    }

    [Fact]
    public async Task S31_a_purpose_outside_the_three_is_refused_for_every_unknown_word()
    {
        await using var t = await StartAsync(("cup", "left"));
        foreach (var purpose in new[] { "play", "request", "other", "" })
        {
            t.Engine.BeginTurn("n");
            await AssertRefusedAsync(t, "move_object", MoveJson("cup_1", purpose, region: "right"), ToolErrorCodes.InvalidArguments, "purpose");
        }
    }
}

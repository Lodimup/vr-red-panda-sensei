using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.World;

/// <summary>highlight_objects (S33, S83) and animate_sensei (S34).</summary>
public sealed class HighlightAnimateTests
{
    private static IReadOnlyList<(IReadOnlyList<string> Ids, int DurationMs)> Highlights(TestEngine t) =>
        t.Events.Of<(IReadOnlyList<string>, int)>("OnHighlight").Select(h => (h.Item1, h.Item2)).ToList();

    private static IReadOnlyList<(string Animation, string? TargetId)> Animations(TestEngine t) =>
        t.Events.Of<(string, string?)>("OnAnimate").Select(a => (a.Item1, a.Item2)).ToList();

    private static string Highlight(int? duration, params string[] ids) =>
        "{\"object_ids\":[" + string.Join(",", ids.Select(i => "\"" + i + "\"")) + "]" + (duration is null ? "" : ",\"duration_ms\":" + duration) + "}";

    // ------------------------------------------------------------------ S33 highlight_objects

    [Fact]
    public async Task S33_unknown_id_is_unknown_object_and_raises_nothing()
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "highlight_objects", Highlight(null, "cup_1", "pen_1"), ToolErrorCodes.UnknownObject, "object_ids[1]");
        Assert.Empty(Highlights(t));
    }

    [Fact]
    public async Task S33_success_raises_on_highlight_with_the_ids_and_the_default_duration()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        var r = await t.CallAsync("highlight_objects", Highlight(null, "pen_1", "cup_1"));
        Assert.True(r.IsSuccess);
        Assert.Equal(new[] { "pen_1", "cup_1" }, r.AffectedIds);
        var h = Assert.Single(Highlights(t));
        Assert.Equal(new[] { "pen_1", "cup_1" }, h.Ids);
        Assert.Equal(2000, h.DurationMs);
    }

    [Fact]
    public async Task S33_a_given_duration_is_passed_through()
    {
        await using var t = await StartAsync(("cup", "left"));
        await t.CallAsync("highlight_objects", Highlight(750, "cup_1"));
        Assert.Equal(750, Assert.Single(Highlights(t)).DurationMs);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(8000, true)]
    [InlineData(2000, true)]
    [InlineData(499, false)]
    [InlineData(8001, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public async Task S33_duration_is_500_to_8000_ms(int duration, bool accepted)
    {
        await using var t = await StartAsync(("cup", "left"));
        if (accepted)
        {
            Assert.True((await t.CallAsync("highlight_objects", Highlight(duration, "cup_1"))).IsSuccess);
            Assert.Equal(duration, Assert.Single(Highlights(t)).DurationMs);
        }
        else
        {
            await AssertRefusedAsync(t, "highlight_objects", Highlight(duration, "cup_1"), ToolErrorCodes.InvalidArguments, "duration_ms");
            Assert.Empty(Highlights(t));
        }
    }

    [Fact]
    public async Task S33_one_to_four_ids_are_accepted_and_zero_or_five_are_not()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "left"), ("book", "left"), ("key", "left"), ("tea", "left"));
        await AssertRefusedAsync(t, "highlight_objects", """{"object_ids":[]}""", ToolErrorCodes.InvalidArguments, "object_ids");
        await AssertRefusedAsync(t, "highlight_objects", Highlight(null, "cup_1", "pen_1", "book_1", "key_1", "tea_1"), ToolErrorCodes.InvalidArguments, "object_ids");
        t.Engine.BeginTurn("n");
        Assert.True((await t.CallAsync("highlight_objects", Highlight(null, "cup_1"))).IsSuccess);
        Assert.True((await t.CallAsync("highlight_objects", Highlight(null, "cup_1", "pen_1", "book_1", "key_1"))).IsSuccess);
        Assert.Equal(2, Highlights(t).Count);
    }

    [Fact]
    public async Task S83_duplicate_ids_in_highlight_objects_are_invalid()
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "highlight_objects", Highlight(null, "cup_1", "cup_1"), ToolErrorCodes.InvalidArguments, "duplicate_id");
        Assert.Empty(Highlights(t));
    }

    [Fact]
    public async Task S33_highlighting_changes_neither_the_revision_nor_the_event_log()
    {
        await using var t = await StartAsync(("cup", "left"));
        var revision = Revision(t);
        var notified = t.Events.SceneEvents.Count;
        var events = SceneLog(t).Count;
        await t.CallAsync("highlight_objects", Highlight(null, "cup_1"));
        Assert.Equal(revision, Revision(t));
        Assert.Equal(events, SceneLog(t).Count);
        Assert.Equal(notified, t.Events.SceneEvents.Count);
    }

    [Fact]
    public async Task S33_without_an_active_task_nothing_is_recorded_and_recorded_assistance_is_null()
    {
        await using var t = await StartAsync(("cup", "left"));
        var r = await t.CallAsync("highlight_objects", Highlight(null, "cup_1"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, Data(r).GetProperty("recorded_assistance").ValueKind);
        Assert.Empty(Aid(t));
    }

    [Fact]
    public async Task S33_highlighting_an_object_the_task_does_not_name_records_nothing()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"), ("book", "center"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        var r = await t.CallAsync("highlight_objects", Highlight(null, "pen_1", "book_1"));
        Assert.True(r.IsSuccess);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, Data(r).GetProperty("recorded_assistance").ValueKind);
        Assert.Empty(Aid(t));
        Assert.Single(Highlights(t));
    }

    [Theory]
    [InlineData("object_id")]
    [InlineData("reference_id")]
    [InlineData("container_id")]
    [InlineData("selector")]
    public async Task S33_highlighting_a_task_answer_records_highlight_answer_for_every_skill_target(string via)
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"), ("bag", "center"));
        var predicate = via switch
        {
            "object_id" => Obj("cup_1", "right"),
            "reference_id" => """{"type":"right_of","object_id":"cup_1","reference_id":"pen_1"}""",
            "container_id" => """{"type":"contained_in","object_id":"cup_1","container_id":"bag_1"}""",
            _ => """{"type":"in_region","selector":{"catalog_id":"pen"},"region":"left"}""",
        };
        var answer = via switch { "object_id" => "cup_1", "reference_id" => "pen_1", "container_id" => "bag_1", _ => "pen_1" };
        await StartTaskAsync(t, Predicates(predicate), targets: "[" + ListenTarget + "," + ProductionTarget + "]");
        var r = await t.CallAsync("highlight_objects", Highlight(null, answer));
        Assert.True(r.IsSuccess);
        Assert.Equal("highlight_answer", Data(r).GetProperty("recorded_assistance").GetString());
        var aid = Aid(t);
        Assert.Equal(2, aid.Count);
        Assert.All(aid, a => Assert.Equal(AssistanceKinds.HighlightAnswer, a.Kind));
        Assert.Equal(new[] { "ringo", "onegai" }, aid.Select(a => a.TargetKey));
        Assert.All(aid, a => Assert.Equal("t1", a.TaskId));
    }

    [Fact]
    public async Task S33_one_referenced_id_among_several_records_the_assistance_once_per_target()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"), ("book", "center"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        await t.CallAsync("highlight_objects", Highlight(null, "book_1", "cup_1", "pen_1"));
        Assert.Single(Aid(t));
    }

    [Fact]
    public async Task S33_a_selector_match_counts_even_when_the_object_is_not_named()
    {
        await using var t = await StartAsync(("cup", "left"), ("cup", "right"), ("pen", "right"));
        await StartTaskAsync(t, Predicates("""{"type":"in_region","selector":{"catalog_id":"cup"},"region":"center"}"""));
        await t.CallAsync("highlight_objects", Highlight(null, "cup_2"));
        Assert.Single(Aid(t));
    }

    [Fact]
    public async Task S33_a_closed_task_no_longer_makes_a_highlight_an_answer()
    {
        await using var t = await StartAsync(("cup", "left"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        await t.CallAsync("update_challenge", """{"task_id":"t1","action":"abandon","reason":"easier"}""");
        var r = await t.CallAsync("highlight_objects", Highlight(null, "cup_1"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, Data(r).GetProperty("recorded_assistance").ValueKind);
        Assert.Empty(Aid(t));
    }

    [Fact]
    public async Task S33_a_refused_highlight_of_a_task_answer_records_nothing()
    {
        await using var t = await StartAsync(("cup", "left"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        await AssertRefusedAsync(t, "highlight_objects", Highlight(null, "cup_1", "pen_1"), ToolErrorCodes.UnknownObject);
        Assert.Empty(Aid(t));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"object_ids":"cup_1"}""")]
    [InlineData("""{"object_ids":["cup_1"],"duration_ms":"long"}""")]
    [InlineData("""{"object_ids":["cup_1"],"duration_ms":1000.5}""")]
    [InlineData("""{"object_ids":["cup_1"],"color":"red"}""")]
    public async Task S21_highlight_objects_arguments_are_strict(string json)
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "highlight_objects", json, ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task S84_a_highlight_cannot_name_an_object_spawned_in_the_same_round()
    {
        await using var t = await StartAsync();
        await t.CallAsync("spawn_objects", SpawnJson(("cup", null, null)));
        var r = await t.CallAsync("highlight_objects", Highlight(null, "cup_1"));
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
        Assert.Empty(Highlights(t));
    }

    [Fact]
    public async Task S69_a_throwing_highlight_handler_does_not_undo_the_call()
    {
        await using var t = await StartAsync(("cup", "left"), ("pen", "right"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        t.Events.ThrowFrom = "OnHighlight";
        var r = await t.CallAsync("highlight_objects", Highlight(null, "cup_1"));
        Assert.True(r.IsSuccess);
        Assert.Single(Aid(t));
        Assert.Contains(t.Trace.Entries, e => e.Tool == "event:OnHighlight" && e.ErrorCode == ToolErrorCodes.EventHandlerError);
    }

    // ------------------------------------------------------------------ S34 animate_sensei

    [Theory]
    [InlineData("idle_float")]
    [InlineData("listen")]
    [InlineData("think")]
    [InlineData("speak")]
    [InlineData("point")]
    [InlineData("encourage")]
    [InlineData("demonstrate")]
    public async Task S34_every_animation_is_accepted_and_raised(string animation)
    {
        await using var t = await StartAsync();
        var r = await t.CallAsync("animate_sensei", $$"""{"animation":"{{animation}}"}""");
        Assert.True(r.IsSuccess);
        Assert.Empty(r.AffectedIds);
        var a = Assert.Single(Animations(t));
        Assert.Equal(animation, a.Animation);
        Assert.Null(a.TargetId);
    }

    [Fact]
    public async Task S34_a_target_is_passed_to_the_event()
    {
        await using var t = await StartAsync(("cup", "left"));
        var r = await t.CallAsync("animate_sensei", """{"animation":"point","target_id":"cup_1"}""");
        Assert.True(r.IsSuccess);
        var a = Assert.Single(Animations(t));
        Assert.Equal(("point", "cup_1"), (a.Animation, a.TargetId));
    }

    [Fact]
    public async Task S34_unknown_target_is_unknown_object_and_raises_nothing()
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "animate_sensei", """{"animation":"point","target_id":"pen_1"}""", ToolErrorCodes.UnknownObject, "pen_1");
        Assert.Empty(Animations(t));
    }

    [Fact]
    public async Task S34_an_unknown_animation_lists_the_allowed_values()
    {
        await using var t = await StartAsync();
        var r = await AssertRefusedAsync(t, "animate_sensei", """{"animation":"dance"}""", ToolErrorCodes.InvalidArguments, "animation");
        Assert.Contains("idle_float", r.Detail);
        Assert.Contains("encourage", r.Detail);
        Assert.Empty(Animations(t));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"animation":"Point"}""")]
    [InlineData("""{"animation":"point","target":"cup_1"}""")]
    [InlineData("""{"animation":"point","target_id":4}""")]
    public async Task S21_animate_sensei_arguments_are_strict(string json)
    {
        await using var t = await StartAsync(("cup", "left"));
        await AssertRefusedAsync(t, "animate_sensei", json, ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task S34_animation_changes_no_revision_event_or_assistance()
    {
        await using var t = await StartAsync(("cup", "left"));
        await StartTaskAsync(t, Predicates(Obj("cup_1", "right")));
        var revision = Revision(t);
        var events = SceneLog(t).Count;
        await t.CallAsync("animate_sensei", """{"animation":"demonstrate","target_id":"cup_1"}""");
        Assert.Equal(revision, Revision(t));
        Assert.Equal(events, SceneLog(t).Count);
        Assert.Empty(Aid(t));
    }

    [Fact]
    public async Task S34_a_null_target_counts_as_omitted()
    {
        await using var t = await StartAsync();
        var r = await t.CallAsync("animate_sensei", """{"animation":"listen","target_id":null}""");
        Assert.True(r.IsSuccess);
        Assert.Null(Assert.Single(Animations(t)).TargetId);
    }
}

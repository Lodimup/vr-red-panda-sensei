using System.Text.Json;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.World;

/// <summary>update_challenge: S36, S104, S106, S108.</summary>
public sealed class UpdateChallengeTests
{
    // apple_1 left, cup_1 center, pen_1 right, bag_1 center
    private static Task<TestEngine> SceneAsync() => StartAsync(("apple", "left"), ("cup", "center"), ("pen", "right"), ("bag", "center"));

    private static string Update(string action, string? reason = "because", string? instruction = null, string? predicates = null, string taskId = "t1")
    {
        var parts = new List<string> { "\"task_id\":\"" + taskId + "\"", "\"action\":\"" + action + "\"" };
        if (reason is not null) parts.Add("\"reason\":\"" + reason + "\"");
        if (instruction is not null) parts.Add("\"instruction\":" + instruction);
        if (predicates is not null) parts.Add("\"predicates\":" + predicates);
        return "{" + string.Join(",", parts) + "}";
    }

    private static TaskView Task1(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.Tasks.History.Single(x => x.TaskId == "t1").Clone();
    }

    // ------------------------------------------------------------------ S36 no active task

    [Fact]
    public async Task S36_without_any_task_it_is_no_active_task()
    {
        await using var t = await SceneAsync();
        await AssertRefusedAsync(t, "update_challenge", Update("abandon"), ToolErrorCodes.NoActiveTask);
    }

    [Fact]
    public async Task S36_a_task_id_that_is_not_the_active_one_is_no_active_task()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await AssertRefusedAsync(t, "update_challenge", Update("abandon", taskId: "t2"), ToolErrorCodes.NoActiveTask, "t2");
        Assert.Equal("t1", t.Engine.GetActiveTask()!.TaskId);
    }

    [Fact]
    public async Task S36_a_completed_task_cannot_be_revised_or_abandoned()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        Assert.True(t.Engine.PlayerMove("apple_1", "right", null).Ok);
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "update_challenge", Update("abandon"), ToolErrorCodes.NoActiveTask);
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "update_challenge", Update("revise", instruction: """{"english":"x"}"""), ToolErrorCodes.NoActiveTask);
    }

    [Fact]
    public async Task S36_an_abandoned_task_cannot_be_abandoned_again()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        Assert.True((await t.CallAsync("update_challenge", Update("abandon"))).IsSuccess);
        await AssertRefusedAsync(t, "update_challenge", Update("abandon"), ToolErrorCodes.NoActiveTask);
    }

    // ------------------------------------------------------------------ S36 revise

    [Fact]
    public async Task S36_revise_without_instruction_or_predicates_is_invalid_arguments()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await AssertRefusedAsync(t, "update_challenge", Update("revise"), ToolErrorCodes.InvalidArguments, "instruction");
    }

    [Fact]
    public async Task S36_revise_with_only_an_instruction_changes_the_text_and_nothing_else()
    {
        await using var t = await SceneAsync();
        var start = await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var before = Task1(t);
        var events = SceneLog(t).Count;
        var revision = Revision(t);
        var r = await t.CallAsync("update_challenge", Update("revise", instruction: """{"english":"Slower: put the apple on the right.","japanese":"りんごを右にね。"}"""));
        Assert.True(r.IsSuccess);
        Assert.Empty(r.AffectedIds);
        var d = Data(r);
        Assert.Equal("t1", d.GetProperty("task_id").GetString());
        Assert.Equal("active", d.GetProperty("status").GetString());
        Assert.Equal("りんごを右にね。", d.GetProperty("instruction").GetProperty("japanese").GetString());
        var after = Task1(t);
        Assert.Equal(before.StartEventId, after.StartEventId);
        Assert.Equal(before.SourceRevision, after.SourceRevision);
        Assert.Equal(before.PredicatesSinceEventId, after.PredicatesSinceEventId);
        Assert.Equal("Slower: put the apple on the right.", after.Instruction.English);
        Assert.Single(after.Predicates);
        Assert.Equal(revision, Revision(t));
        Assert.Equal(events + 1, SceneLog(t).Count);
        Assert.Equal("task_revised", SceneLog(t).Last().Kind);
        Assert.Equal(Data(start).GetProperty("start_event_id").GetInt64(), d.GetProperty("start_event_id").GetInt64());
    }

    [Fact]
    public async Task S36_revise_predicates_replaces_them_and_resets_source_revision()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        t.Engine.MarkRoundStart();
        await t.CallAsync("move_object", MoveJson("pen_1", "arrange", region: "left"));
        var revision = Revision(t);
        var r = await t.CallAsync("update_challenge", Update("revise", predicates: Predicates(Obj("cup_1", "right"))));
        Assert.True(r.IsSuccess);
        var after = Task1(t);
        var p = Assert.Single(after.Predicates);
        Assert.Equal("cup_1", p.ObjectId);
        Assert.Equal(revision, after.SourceRevision);
        Assert.Equal(SceneLog(t).Last().EventId, after.PredicatesSinceEventId);
        Assert.True(after.PredicatesSinceEventId > after.StartEventId);
        Assert.Equal("t1", after.TaskId);
        Assert.Equal(TaskStatuses.Active, after.Status);
    }

    [Fact]
    public async Task S108_revise_keeps_the_assistance_boundary_baseline_and_fresh_variation_flag()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")), extra: "\"is_fresh_variation\":true");
        var before = Task1(t);
        await t.Engine.SetPreferenceByPlayerAsync(new Sensei.Engine.Learner.SupportPatch { ShowRomaji = false });
        t.Engine.BeginTurn("n");
        await t.CallAsync("update_challenge", Update("revise", instruction: """{"english":"again"}""", predicates: Predicates(Obj("apple_1", "center"))));
        var after = Task1(t);
        Assert.Equal(before.StartEventId, after.StartEventId);
        Assert.True(after.AssistanceBaseline.ShowRomaji);
        Assert.True(after.IsFreshVariation);
        Assert.Equal(before.SkillTargets.Select(x => x.Key), after.SkillTargets.Select(x => x.Key));
    }

    [Fact]
    public async Task S36_revised_predicates_are_validated_like_start_challenge()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var before = Task1(t);
        await AssertRefusedAsync(t, "update_challenge", Update("revise", predicates: Predicates(Obj("book_1", "left"))), ToolErrorCodes.ImpossibleTask, "book_1");
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "update_challenge", Update("revise", predicates: Predicates(Obj("apple_1", "left"))), ToolErrorCodes.ImpossibleTask, "already true");
        var after = Task1(t);
        Assert.Equal(before.Predicates[0].Region, after.Predicates[0].Region);
        Assert.Equal(before.SourceRevision, after.SourceRevision);
        Assert.Single(t.Events.Tasks);
    }

    [Theory]
    [InlineData("""[{"type":"in_region","object_id":"apple_1","region":"right"},{"type":"in_region","object_id":"apple_1","region":"center"}]""")]
    [InlineData("""[{"type":"unchanged","object_id":"apple_1"}]""")]
    [InlineData("""[{"type":"contained_in","object_id":"bag_1","container_id":"bag_1"}]""")]
    public async Task S106_revise_applies_the_conjunction_checks(string predicates)
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await AssertRefusedAsync(t, "update_challenge", Update("revise", predicates: predicates), ToolErrorCodes.ImpossibleTask);
    }

    [Fact]
    public async Task S36_revise_does_not_raise_task_already_active()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var r = await t.CallAsync("update_challenge", Update("revise", predicates: Predicates(Obj("apple_1", "center"))));
        Assert.True(r.IsSuccess, r.Detail);
    }

    [Fact]
    public async Task S36_revised_predicates_may_use_the_tasks_scenario_properties()
    {
        await using var t = await SceneAsync();
        var first = Predicates(Obj("pen_1", "left"));
        await StartTaskAsync(t, first, extra: "\"scenario_properties\":[{\"object_id\":\"cup_1\",\"name\":\"content\",\"value\":\"hot\"}]");
        var selector = Predicates("""{"type":"in_region","selector":{"catalog_id":"cup","property":{"name":"content","value":"hot"}},"region":"right"}""");
        Assert.True((await t.CallAsync("update_challenge", Update("revise", predicates: selector))).IsSuccess);
        t.Engine.BeginTurn("n");
        var undeclared = Predicates("""{"type":"in_region","selector":{"catalog_id":"cup","property":{"name":"content","value":"cold"}},"region":"right"}""");
        await AssertRefusedAsync(t, "update_challenge", Update("revise", predicates: undeclared), ToolErrorCodes.ImpossibleTask, "content=cold");
    }

    [Fact]
    public async Task S36_a_revision_that_is_already_satisfied_is_refused_but_one_that_becomes_true_by_play_completes()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await t.CallAsync("update_challenge", Update("revise", predicates: Predicates(Obj("pen_1", "left"))));
        Assert.True(t.Engine.PlayerMove("pen_1", "left", null).Ok);
        Assert.Equal(TaskStatuses.Completed, Task1(t).Status);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"japanese":""" + "\"" + "あ" + "\"" + """,}""")]
    public async Task S36_a_revised_instruction_needs_a_language(string instruction)
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await AssertRefusedAsync(t, "update_challenge", Update("revise", instruction: instruction), ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task S36_a_revised_predicate_with_a_wrong_field_for_its_type_is_invalid_arguments()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await AssertRefusedAsync(t, "update_challenge", Update("revise", predicates: """[{"type":"selected","object_id":"apple_1","region":"left"}]"""), ToolErrorCodes.InvalidArguments, "predicates[0]");
    }

    [Fact]
    public async Task S36_revise_raises_on_task_changed()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await t.CallAsync("update_challenge", Update("revise", instruction: """{"english":"new text"}"""));
        Assert.Equal(2, t.Events.Tasks.Count);
        Assert.Equal("new text", t.Events.Tasks.Last().Instruction.English);
        Assert.Equal(TaskStatuses.Active, t.Events.Tasks.Last().Status);
    }

    // ------------------------------------------------------------------ S36 abandon

    [Fact]
    public async Task S36_abandon_sets_status_abandoned_clears_the_active_task_and_raises_on_task_changed()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var revision = Revision(t);
        var r = await t.CallAsync("update_challenge", Update("abandon", "too hard for now"));
        Assert.True(r.IsSuccess);
        Assert.Equal("abandoned", Data(r).GetProperty("status").GetString());
        Assert.Null(t.Engine.GetActiveTask());
        Assert.Null(t.Engine.GetScene().ActiveTask);
        Assert.Equal(TaskStatuses.Abandoned, Task1(t).Status);
        Assert.Equal(TaskStatuses.Abandoned, t.Events.Tasks.Last().Status);
        Assert.Equal(revision, Revision(t));
        Assert.Equal("task_abandoned", SceneLog(t).Last().Kind);
    }

    [Fact]
    public async Task S36_abandon_ignores_nothing_it_still_validates_a_given_instruction_and_predicates_shape()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await AssertRefusedAsync(t, "update_challenge", Update("abandon", instruction: "{}"), ToolErrorCodes.InvalidArguments);
        Assert.NotNull(t.Engine.GetActiveTask());
    }

    [Fact]
    public async Task S36_after_abandon_the_playing_field_no_longer_completes_it()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await t.CallAsync("update_challenge", Update("abandon"));
        Assert.True(t.Engine.PlayerMove("apple_1", "right", null).Ok);
        Assert.Equal(TaskStatuses.Abandoned, Task1(t).Status);
    }

    // ------------------------------------------------------------------ bounds and strictness

    [Theory]
    [InlineData(1, true)]
    [InlineData(120, true)]
    [InlineData(0, false)]
    [InlineData(121, false)]
    public async Task S36_reason_is_1_to_120_characters(int length, bool accepted)
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var json = Update("abandon", new string('r', length));
        if (accepted) Assert.True((await t.CallAsync("update_challenge", json)).IsSuccess);
        else await AssertRefusedAsync(t, "update_challenge", json, ToolErrorCodes.InvalidArguments, "reason");
    }

    [Theory]
    [InlineData("""{"task_id":"t1","action":"abandon"}""", "reason")]
    [InlineData("""{"action":"abandon","reason":"x"}""", "task_id")]
    [InlineData("""{"task_id":"t1","reason":"x"}""", "action")]
    [InlineData("""{"task_id":"t1","action":"pause","reason":"x"}""", "action")]
    [InlineData("""{"task_id":"t1","action":"Abandon","reason":"x"}""", "action")]
    [InlineData("""{"task_id":"t1","action":"abandon","reason":"x","extra":1}""", "extra")]
    [InlineData("""{"task_id":1,"action":"abandon","reason":"x"}""", "task_id")]
    [InlineData("""{"task_id":"t1","action":"revise","reason":"x","predicates":[]}""", "predicates")]
    public async Task S21_update_challenge_arguments_are_strict(string json, string path)
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await AssertRefusedAsync(t, "update_challenge", json, ToolErrorCodes.InvalidArguments, path);
    }

    [Fact]
    public async Task S84_revised_predicates_cannot_name_an_object_spawned_in_the_same_round()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await t.CallAsync("spawn_objects", SpawnJson(("book", "left", null)));
        var r = await t.CallAsync("update_challenge", Update("revise", predicates: Predicates(Obj("book_1", "right"))));
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("update_challenge", Update("revise", predicates: Predicates(Obj("book_1", "right"))))).IsSuccess);
    }
}

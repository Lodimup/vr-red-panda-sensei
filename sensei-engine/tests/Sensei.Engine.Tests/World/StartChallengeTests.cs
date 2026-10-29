using System.Text.Json;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.World;

/// <summary>start_challenge: S35a–d, S104, S105, S106 on top of the foundation's task validation.</summary>
public sealed class StartChallengeTests
{
    // apple_1 left, cup_1 center, pen_1 right, bag_1 center, box_1 right
    private static Task<TestEngine> SceneAsync() =>
        StartAsync(("apple", "left"), ("cup", "center"), ("pen", "right"), ("bag", "center"), ("box", "right"));

    private static IReadOnlyList<TaskView> History(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.Tasks.History.Select(x => x.Clone()).ToList();
    }

    // ------------------------------------------------------------------ S35a instruction and single task

    [Fact]
    public async Task S35a_an_instruction_with_neither_language_is_invalid_arguments()
    {
        await using var t = await SceneAsync();
        await AssertRefusedAsync(t, "start_challenge", StartJson(Predicates(Obj("apple_1", "right")), instruction: "{}"), ToolErrorCodes.InvalidArguments, "instruction");
        Assert.Null(t.Engine.GetActiveTask());
    }

    [Theory]
    [InlineData("""{"japanese":"りんごを右に置いて。"}""")]
    [InlineData("""{"english":"Put the apple on the right."}""")]
    [InlineData("""{"japanese":"りんごを右に置いて。","english":"Put the apple on the right."}""")]
    public async Task S35a_either_or_both_instruction_languages_are_accepted(string instruction)
    {
        await using var t = await SceneAsync();
        var r = await t.CallAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right")), instruction: instruction));
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task S35a_a_second_start_while_one_is_active_is_task_already_active()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        await AssertRefusedAsync(t, "start_challenge", StartJson(Predicates(Obj("cup_1", "left"))), ToolErrorCodes.TaskAlreadyActive, "t1");
        Assert.Equal("t1", t.Engine.GetActiveTask()!.TaskId);
        Assert.Single(History(t));
    }

    [Fact]
    public async Task S35a_a_new_task_can_start_after_the_last_was_abandoned_or_completed()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        Assert.True((await t.CallAsync("update_challenge", """{"task_id":"t1","action":"abandon","reason":"try another"}""")).IsSuccess);
        var second = await t.CallAsync("start_challenge", StartJson(Predicates(Obj("cup_1", "left"))));
        Assert.True(second.IsSuccess);
        Assert.Equal("t2", Data(second).GetProperty("task_id").GetString());
        Assert.True(t.Engine.PlayerMove("cup_1", "left", null).Ok);
        t.Engine.BeginTurn("again");
        var third = await t.CallAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right"))));
        Assert.Equal("t3", Data(third).GetProperty("task_id").GetString());
    }

    // ------------------------------------------------------------------ S35c success shape

    [Fact]
    public async Task S35c_success_returns_the_task_view_in_data()
    {
        await using var t = await SceneAsync();
        t.Engine.PlayerSelect("apple_1");
        t.Engine.BeginTurn("new round");
        var revision = Revision(t);
        var extra = "\"role\":\"shopkeeper\",\"is_fresh_variation\":true,\"scenario_properties\":[{\"object_id\":\"cup_1\",\"name\":\"content\",\"value\":\"hot\"}]";
        var predicates = Predicates("""{"type":"in_region","selector":{"catalog_id":"cup","property":{"name":"content","value":"hot"}},"region":"right"}""");
        var r = await t.CallAsync("start_challenge", StartJson(predicates, family: "converse_roleplay", instruction: """{"japanese":"熱いのをください。","english":"Give me the hot one."}""", extra: extra));
        Assert.True(r.IsSuccess);
        Assert.Empty(r.AffectedIds);
        var d = Data(r);
        Assert.Equal("t1", d.GetProperty("task_id").GetString());
        Assert.Equal("active", d.GetProperty("status").GetString());
        Assert.Equal("converse_roleplay", d.GetProperty("family").GetString());
        Assert.Equal("熱いのをください。", d.GetProperty("instruction").GetProperty("japanese").GetString());
        Assert.Equal("Give me the hot one.", d.GetProperty("instruction").GetProperty("english").GetString());
        var target = Assert.Single(d.GetProperty("skill_targets").EnumerateArray());
        Assert.Equal("listening", target.GetProperty("dimension").GetString());
        Assert.Equal("ringo", target.GetProperty("key").GetString());
        Assert.Equal("りんご", target.GetProperty("japanese").GetString());
        var predicate = Assert.Single(d.GetProperty("predicates").EnumerateArray());
        Assert.Equal("in_region", predicate.GetProperty("type").GetString());
        Assert.Equal("cup", predicate.GetProperty("selector").GetProperty("catalog_id").GetString());
        var scenario = Assert.Single(d.GetProperty("scenario_properties").EnumerateArray());
        Assert.Equal("cup_1", scenario.GetProperty("object_id").GetString());
        Assert.Equal("shopkeeper", d.GetProperty("role").GetString());
        Assert.True(d.GetProperty("is_fresh_variation").GetBoolean());
        Assert.Equal(revision, d.GetProperty("source_revision").GetInt64());
        var baseline = d.GetProperty("assistance_baseline");
        Assert.True(baseline.GetProperty("show_japanese").GetBoolean());
        Assert.Equal("normal", baseline.GetProperty("pace").GetString());
    }

    [Fact]
    public async Task S35c_omitted_optionals_default_to_empty_false_and_absent()
    {
        await using var t = await SceneAsync();
        var d = Data(await StartTaskAsync(t, Predicates(Obj("apple_1", "right"))));
        Assert.Equal(0, d.GetProperty("scenario_properties").GetArrayLength());
        Assert.False(d.GetProperty("is_fresh_variation").GetBoolean());
        Assert.True(!d.TryGetProperty("role", out var role) || role.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task S35c_start_raises_on_task_changed_and_is_the_engines_active_task()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var raised = Assert.Single(t.Events.Tasks);
        Assert.Equal("t1", raised.TaskId);
        Assert.Equal(TaskStatuses.Active, raised.Status);
        var active = t.Engine.GetActiveTask();
        Assert.NotNull(active);
        Assert.Equal("t1", active!.TaskId);
        Assert.Equal("t1", t.Engine.GetScene().ActiveTask!.TaskId);
    }

    [Fact]
    public async Task S104_start_appends_task_started_without_changing_the_revision()
    {
        await using var t = await SceneAsync();
        var revision = Revision(t);
        var lastEvent = SceneLog(t).Last().EventId;
        var r = await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        Assert.Equal(revision, Revision(t));
        Assert.Equal(revision, r.Revision);
        var e = SceneLog(t).Last();
        Assert.Equal("task_started", e.Kind);
        Assert.Equal(lastEvent + 1, e.EventId);
        Assert.Equal(revision, e.Revision);
        Assert.Equal(e.EventId, Data(r).GetProperty("start_event_id").GetInt64());
        Assert.Equal(revision, Data(r).GetProperty("source_revision").GetInt64());
    }

    [Fact]
    public async Task S35c_the_assistance_baseline_is_a_copy_of_the_support_settings_at_start()
    {
        await using var t = await SceneAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new Sensei.Engine.Learner.SupportPatch { ShowRomaji = false, Pace = "slow" });
        t.Engine.BeginTurn("n");
        var d = Data(await StartTaskAsync(t, Predicates(Obj("apple_1", "right"))));
        var b = d.GetProperty("assistance_baseline");
        Assert.False(b.GetProperty("show_romaji").GetBoolean());
        Assert.Equal("slow", b.GetProperty("pace").GetString());
    }

    // ------------------------------------------------------------------ argument bounds

    [Theory]
    [InlineData(80, true)]
    [InlineData(81, false)]
    public async Task S35_instruction_japanese_is_at_most_80_characters(int length, bool accepted)
    {
        await using var t = await SceneAsync();
        var instruction = "{\"japanese\":\"" + new string('あ', length) + "\"}";
        await AssertLengthAsync(t, StartJson(Predicates(Obj("apple_1", "right")), instruction: instruction), accepted, "instruction.japanese");
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public async Task S35_instruction_english_is_at_most_200_characters(int length, bool accepted)
    {
        await using var t = await SceneAsync();
        var instruction = "{\"english\":\"" + new string('a', length) + "\"}";
        await AssertLengthAsync(t, StartJson(Predicates(Obj("apple_1", "right")), instruction: instruction), accepted, "instruction.english");
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(41, false)]
    [InlineData(0, false)]
    public async Task S35_skill_target_key_is_1_to_40_characters(int length, bool accepted)
    {
        await using var t = await SceneAsync();
        var target = "{\"dimension\":\"vocabulary\",\"key\":\"" + new string('k', length) + "\"}";
        await AssertLengthAsync(t, StartJson(Predicates(Obj("apple_1", "right")), targets: "[" + target + "]"), accepted, "skill_targets[0].key");
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public async Task S35_skill_target_japanese_is_at_most_30_characters(int length, bool accepted)
    {
        await using var t = await SceneAsync();
        var target = "{\"dimension\":\"vocabulary\",\"key\":\"k\",\"japanese\":\"" + new string('い', length) + "\"}";
        await AssertLengthAsync(t, StartJson(Predicates(Obj("apple_1", "right")), targets: "[" + target + "]"), accepted, "skill_targets[0].japanese");
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(41, false)]
    public async Task S35_role_is_at_most_40_characters(int length, bool accepted)
    {
        await using var t = await SceneAsync();
        var extra = "\"role\":\"" + new string('r', length) + "\"";
        await AssertLengthAsync(t, StartJson(Predicates(Obj("apple_1", "right")), extra: extra), accepted, "role");
    }

    private static async Task AssertLengthAsync(TestEngine t, string json, bool accepted, string path)
    {
        if (accepted) Assert.True((await t.CallAsync("start_challenge", json)).IsSuccess);
        else await AssertRefusedAsync(t, "start_challenge", json, ToolErrorCodes.InvalidArguments, path);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task S35_skill_targets_are_one_to_three(int count, bool accepted)
    {
        await using var t = await SceneAsync();
        var targets = "[" + string.Join(",", Enumerable.Range(0, count).Select(i => "{\"dimension\":\"vocabulary\",\"key\":\"k" + i + "\"}")) + "]";
        await AssertLengthAsync(t, StartJson(Predicates(Obj("apple_1", "right")), targets: targets), accepted, "skill_targets");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task S35_predicates_are_one_to_three(int count, bool accepted)
    {
        await using var t = await SceneAsync();
        var all = new[] { Obj("apple_1", "right"), Obj("cup_1", "left"), Obj("pen_1", "center"), Obj("bag_1", "left") };
        await AssertLengthAsync(t, StartJson(Predicates(all.Take(count).ToArray())), accepted, "predicates");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public async Task S35_scenario_properties_are_zero_to_four(int count, bool accepted)
    {
        await using var t = await SceneAsync();
        var ids = new[] { "apple_1", "cup_1", "pen_1", "bag_1", "box_1" };
        var props = "[" + string.Join(",", ids.Take(count).Select(id => "{\"object_id\":\"" + id + "\",\"name\":\"state\",\"value\":\"new\"}")) + "]";
        await AssertLengthAsync(t, StartJson(Predicates(Obj("apple_1", "right")), extra: "\"scenario_properties\":" + props), accepted, "scenario_properties");
    }

    [Theory]
    [InlineData("Color", false)]
    [InlineData("color2", false)]
    [InlineData("", false)]
    [InlineData("a_b", true)]
    [InlineData("_", true)]
    public async Task S35_scenario_property_names_are_lowercase_letters_and_underscores(string name, bool accepted)
    {
        await using var t = await SceneAsync();
        var extra = "\"scenario_properties\":[{\"object_id\":\"cup_1\",\"name\":\"" + name + "\",\"value\":\"x\"}]";
        await AssertLengthAsync(t, StartJson(Predicates(Obj("apple_1", "right")), extra: extra), accepted, "scenario_properties[0].name");
    }

    [Theory]
    [InlineData(24, true)]
    [InlineData(25, false)]
    [InlineData(0, false)]
    public async Task S35_scenario_property_values_are_1_to_24_characters(int length, bool accepted)
    {
        await using var t = await SceneAsync();
        var extra = "\"scenario_properties\":[{\"object_id\":\"cup_1\",\"name\":\"state\",\"value\":\"" + new string('v', length) + "\"}]";
        await AssertLengthAsync(t, StartJson(Predicates(Obj("apple_1", "right")), extra: extra), accepted, "scenario_properties[0].value");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(0, false)]
    [InlineData(9, false)]
    public async Task S35_quantity_count_is_one_to_eight(int count, bool accepted)
    {
        await using var t = await StartAsync(("apple", "left"), ("apple", "left"), ("apple", "left"), ("apple", "left"), ("apple", "left"),
            ("apple", "left"), ("apple", "left"), ("apple", "left"), ("pen", "right"));
        var quantity = "{\"type\":\"quantity\",\"selector\":{\"catalog_id\":\"apple\"},\"region\":\"right\",\"count\":" + count + ",\"comparison\":\"lte\"}";
        // lte over 0 apples on the right is true at start, so pair it with a predicate that is not
        await AssertLengthAsync(t, StartJson(Predicates(quantity, Obj("pen_1", "left"))), accepted, "count");
    }

    [Theory]
    [InlineData("""{"family":"free_play","instruction":{"english":"x"},"skill_targets":[{"dimension":"vocabulary","key":"k"}],"predicates":[{"type":"selected","object_id":"apple_1"}]}""", "family")]
    [InlineData("""{"family":"listen_arrange","instruction":{"english":"x"},"skill_targets":[{"dimension":"grammar","key":"k"}],"predicates":[{"type":"selected","object_id":"apple_1"}]}""", "dimension")]
    [InlineData("""{"family":"listen_arrange","instruction":{"english":"x"},"skill_targets":[{"dimension":"vocabulary","key":"k"}],"predicates":[{"type":"hovered","object_id":"apple_1"}]}""", "predicates[0].type")]
    [InlineData("""{"instruction":{"english":"x"},"skill_targets":[{"dimension":"vocabulary","key":"k"}],"predicates":[{"type":"selected","object_id":"apple_1"}]}""", "family")]
    [InlineData("""{"family":"listen_arrange","skill_targets":[{"dimension":"vocabulary","key":"k"}],"predicates":[{"type":"selected","object_id":"apple_1"}]}""", "instruction")]
    [InlineData("""{"family":"listen_arrange","instruction":{"english":"x"},"predicates":[{"type":"selected","object_id":"apple_1"}]}""", "skill_targets")]
    [InlineData("""{"family":"listen_arrange","instruction":{"english":"x"},"skill_targets":[{"dimension":"vocabulary","key":"k"}]}""", "predicates")]
    [InlineData("""{"family":"listen_arrange","instruction":{"english":"x"},"skill_targets":[{"dimension":"vocabulary","key":"k"}],"predicates":[{"type":"selected","object_id":"apple_1"}],"difficulty":2}""", "difficulty")]
    public async Task S21_start_challenge_arguments_are_strict(string json, string path)
    {
        await using var t = await SceneAsync();
        await AssertRefusedAsync(t, "start_challenge", json, ToolErrorCodes.InvalidArguments, path);
    }

    [Theory]
    [InlineData("""{"type":"selected","object_id":"apple_1","region":"left"}""", "predicates[0]")]
    [InlineData("""{"type":"selected"}""", "object_id")]
    [InlineData("""{"type":"in_region","object_id":"apple_1"}""", "region")]
    [InlineData("""{"type":"in_region","region":"left"}""", "predicates[0]")]
    [InlineData("""{"type":"in_region","object_id":"apple_1","selector":{"catalog_id":"apple"},"region":"left"}""", "exactly one")]
    [InlineData("""{"type":"contained_in","object_id":"cup_1"}""", "container_id")]
    [InlineData("""{"type":"quantity","selector":{"catalog_id":"cup"},"count":1}""", "exactly one of region or container_id")]
    [InlineData("""{"type":"quantity","selector":{"catalog_id":"cup"},"region":"left","container_id":"bag_1","count":1}""", "exactly one of region or container_id")]
    [InlineData("""{"type":"quantity","region":"left","count":1}""", "selector")]
    [InlineData("""{"type":"quantity","selector":{"catalog_id":"cup"},"region":"left"}""", "count")]
    [InlineData("""{"type":"left_of","object_id":"apple_1"}""", "reference_id")]
    [InlineData("""{"type":"before","reference_id":"apple_1"}""", "object_id")]
    [InlineData("""{"type":"unchanged","object_id":"apple_1","reference_id":"cup_1"}""", "predicates[0]")]
    public async Task S21_predicate_fields_follow_the_type_table(string predicate, string path)
    {
        await using var t = await SceneAsync();
        await AssertRefusedAsync(t, "start_challenge", StartJson("[" + predicate + "]"), ToolErrorCodes.InvalidArguments, path);
    }

    [Theory]
    [InlineData("""{"type":"selected","object_id":"apple_1"}""")]
    [InlineData("""{"type":"in_region","object_id":"apple_1","region":"right"}""")]
    [InlineData("""{"type":"in_region","selector":{"catalog_id":"apple"},"region":"right"}""")]
    [InlineData("""{"type":"in_region","object_id":"apple_1","region":"learner_hand"}""")]
    [InlineData("""{"type":"contained_in","object_id":"cup_1","container_id":"bag_1"}""")]
    [InlineData("""{"type":"contained_in","selector":{"catalog_id":"cup"},"container_id":"box_1"}""")]
    [InlineData("""{"type":"quantity","selector":{"catalog_id":"cup"},"region":"right","count":1}""")]
    [InlineData("""{"type":"quantity","selector":{"catalog_id":"cup"},"container_id":"bag_1","count":1,"comparison":"gte"}""")]
    [InlineData("""{"type":"left_of","object_id":"cup_1","reference_id":"apple_1"}""")]
    [InlineData("""{"type":"right_of","object_id":"apple_1","reference_id":"cup_1"}""")]
    [InlineData("""{"type":"before","object_id":"cup_1","reference_id":"apple_1"}""")]
    public async Task S35_every_predicate_type_can_start_a_task(string predicate)
    {
        await using var t = await SceneAsync();
        var r = await t.CallAsync("start_challenge", StartJson("[" + predicate + "]"));
        Assert.True(r.IsSuccess, r.Detail);
    }

    [Fact]
    public async Task S35b_unchanged_may_accompany_a_predicate_that_is_not_yet_true()
    {
        await using var t = await SceneAsync();
        var r = await t.CallAsync("start_challenge", StartJson(Predicates("""{"type":"unchanged","object_id":"apple_1"}""", Obj("cup_1", "left"))));
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task S35b_a_lte_quantity_may_exceed_the_matching_instances()
    {
        await using var t = await SceneAsync();
        var quantity = """{"type":"quantity","selector":{"catalog_id":"cup"},"region":"left","count":5,"comparison":"lte"}""";
        var r = await t.CallAsync("start_challenge", StartJson(Predicates(quantity, Obj("cup_1", "left"))));
        Assert.True(r.IsSuccess, r.Detail);
    }

    // ------------------------------------------------------------------ S35b / S104 / S106 impossible_task

    [Theory]
    [InlineData("""[{"type":"in_region","object_id":"book_1","region":"left"}]""", null, "book_1")]
    [InlineData("""[{"type":"contained_in","object_id":"apple_1","container_id":"pen_1"}]""", null, "pen_1")]
    [InlineData("""[{"type":"contained_in","object_id":"apple_1","container_id":"bag_9"}]""", null, "bag_9")]
    [InlineData("""[{"type":"right_of","object_id":"apple_1","reference_id":"apple_1"}]""", null, "same object")]
    [InlineData("""[{"type":"in_region","selector":{"catalog_id":"sword"},"region":"left"}]""", null, "sword")]
    [InlineData("""[{"type":"in_region","selector":{"catalog_id":"cup","property":{"name":"color","value":"red"}},"region":"left"}]""", null, "color")]
    [InlineData("""[{"type":"quantity","selector":{"catalog_id":"cup"},"region":"left","count":2}]""", null, "needs 2")]
    [InlineData("""[{"type":"quantity","selector":{"catalog_id":"cup"},"region":"left","count":2,"comparison":"gte"}]""", null, "needs 2")]
    [InlineData("""[{"type":"in_region","object_id":"apple_1","region":"left"}]""", null, "already true")]
    [InlineData("""[{"type":"selected","object_id":"apple_1"},{"type":"in_region","object_id":"cup_1","region":"center"}]""", "selected", "already true")]
    [InlineData("""[{"type":"unchanged","object_id":"apple_1"}]""", null, "unchanged")]
    [InlineData("""[{"type":"in_region","object_id":"apple_1","region":"right"}]""", "scenario_properties:book_1", "book_1")]
    public async Task S35b_impossible_tasks_are_refused_and_nothing_starts(string predicates, string? mode, string detail)
    {
        await using var t = await SceneAsync();
        if (mode == "selected") t.Engine.PlayerSelect("apple_1");
        string? extra = null;
        if (mode == "scenario_properties:book_1") extra = "\"scenario_properties\":[{\"object_id\":\"book_1\",\"name\":\"state\",\"value\":\"new\"}]";
        if (mode == "selected") t.Engine.BeginTurn("n");
        var events = SceneLog(t).Count;
        await AssertRefusedAsync(t, "start_challenge", StartJson(predicates, extra: extra), ToolErrorCodes.ImpossibleTask, detail);
        Assert.Null(t.Engine.GetActiveTask());
        Assert.Empty(History(t));
        Assert.Equal(events, SceneLog(t).Count);
        Assert.Empty(t.Events.Tasks);
    }

    [Theory]
    [InlineData("""[{"type":"in_region","object_id":"apple_1","region":"right"},{"type":"in_region","object_id":"apple_1","region":"center"}]""")]
    [InlineData("""[{"type":"in_region","object_id":"apple_1","region":"right"},{"type":"contained_in","object_id":"apple_1","container_id":"bag_1"}]""")]
    [InlineData("""[{"type":"contained_in","object_id":"apple_1","container_id":"bag_1"},{"type":"contained_in","object_id":"apple_1","container_id":"box_1"}]""")]
    [InlineData("""[{"type":"contained_in","object_id":"bag_1","container_id":"box_1"}]""")]
    [InlineData("""[{"type":"left_of","object_id":"cup_1","reference_id":"pen_1"},{"type":"right_of","object_id":"cup_1","reference_id":"pen_1"}]""")]
    [InlineData("""[{"type":"before","object_id":"cup_1","reference_id":"pen_1"},{"type":"before","object_id":"pen_1","reference_id":"cup_1"}]""")]
    public async Task S106_conjunctions_that_cannot_hold_together_are_impossible_task(string predicates)
    {
        await using var t = await SceneAsync();
        await AssertRefusedAsync(t, "start_challenge", StartJson(predicates), ToolErrorCodes.ImpossibleTask);
        Assert.Null(t.Engine.GetActiveTask());
    }

    [Fact]
    public async Task S106_a_positional_predicate_for_an_object_inside_a_container_is_impossible_task()
    {
        await using var t = await SceneAsync();
        Assert.True(t.Engine.PlayerMove("cup_1", null, "bag_1").Ok);
        t.Engine.BeginTurn("n");
        await AssertRefusedAsync(t, "start_challenge", StartJson(Predicates("""{"type":"left_of","object_id":"cup_1","reference_id":"pen_1"}""")), ToolErrorCodes.ImpossibleTask);
    }

    [Fact]
    public async Task S104_a_task_of_unchanged_predicates_only_is_impossible_task()
    {
        await using var t = await SceneAsync();
        var both = Predicates("""{"type":"unchanged","object_id":"apple_1"}""", """{"type":"unchanged","object_id":"cup_1"}""");
        await AssertRefusedAsync(t, "start_challenge", StartJson(both), ToolErrorCodes.ImpossibleTask, "unchanged");
    }

    [Fact]
    public async Task S35b_impossible_task_is_checked_after_task_already_active()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var r = await t.CallAsync("start_challenge", StartJson(Predicates(Obj("book_1", "left"))));
        Assert.Equal(ToolErrorCodes.TaskAlreadyActive, r.ErrorCode);
    }

    [Fact]
    public async Task S84_a_task_cannot_name_an_object_spawned_in_the_same_round()
    {
        await using var t = await StartAsync();
        await t.CallAsync("spawn_objects", SpawnJson(("apple", "left", null)));
        var r = await t.CallAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right"))));
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal(ToolDetails.IdNotYetVisible, r.Detail);
        Assert.Null(t.Engine.GetActiveTask());
        t.Engine.MarkRoundStart();
        Assert.True((await t.CallAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right"))))).IsSuccess);
    }

    [Fact]
    public async Task Discover_request_can_select_a_same_round_spawn_and_complete_on_delivery()
    {
        await using var t = await StartAsync();
        Assert.True((await t.CallAsync("spawn_objects", SpawnJson(("water", "center", null)))).IsSuccess);
        var args = "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"Ask for water\"},\"skill_targets\":[{\"dimension\":\"vocabulary\",\"key\":\"water\"}],\"predicates\":[{\"type\":\"in_region\",\"selector\":{\"catalog_id\":\"water\"},\"region\":\"learner_hand\"}]}";
        var start = await t.CallAsync("start_challenge", args);
        Assert.True(start.IsSuccess, start.Detail);
        t.Engine.MarkRoundStart();
        var move = await t.CallAsync("move_object", MoveJson("water_1", "fulfill_request", region: "learner_hand"));
        Assert.True(move.IsSuccess, move.Detail);
        Assert.Equal("completed", Data(move).GetProperty("task_status").GetString());
    }

    // ------------------------------------------------------------------ S35d evaluation through tools and player actions

    [Fact]
    public async Task S35d_a_sensei_move_that_makes_every_predicate_true_completes_the_task()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        t.Engine.MarkRoundStart();
        var before = Revision(t);
        var r = await t.CallAsync("move_object", MoveJson("apple_1", "fulfill_request", region: "right"));
        Assert.True(r.IsSuccess);
        Assert.Equal("completed", Data(r).GetProperty("task_status").GetString());
        Assert.Null(t.Engine.GetActiveTask());
        Assert.Equal(before + 1, Revision(t));
        var task = History(t).Single();
        Assert.Equal(TaskStatuses.Completed, task.Status);
        Assert.Equal("sensei", task.CompletedBy);
        var log = SceneLog(t);
        Assert.Equal("task_completed", log.Last().Kind);
        Assert.Equal(before + 1, log.Last().Revision);
        Assert.Equal(log[^2].EventId, task.CompletedEventId);
        Assert.Equal(TaskStatuses.Completed, t.Events.Tasks.Last().Status);
    }

    [Fact]
    public async Task S35d_a_player_move_completes_the_task_and_the_next_sensei_call_is_not_marked_completed()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        Assert.True(t.Engine.PlayerMove("apple_1", "right", null).Ok);
        var task = History(t).Single();
        Assert.Equal(TaskStatuses.Completed, task.Status);
        Assert.Equal("player", task.CompletedBy);
        Assert.Equal("task_completed", SceneLog(t).Last().Kind);
        Assert.Equal("player", SceneLog(t).Last().Actor);
        t.Engine.BeginTurn("after");
        var r = await t.CallAsync("inspect_scene", "{}");
        Assert.False(Data(r).TryGetProperty("task_status", out _));
    }

    [Fact]
    public async Task S105_a_player_selection_completes_a_selected_task_without_a_revision_change()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Selected("cup_1")));
        var before = Revision(t);
        Assert.True(t.Engine.PlayerSelect("cup_1").Ok);
        Assert.Equal(before, Revision(t));
        var task = History(t).Single();
        Assert.Equal(TaskStatuses.Completed, task.Status);
        Assert.Equal("player", task.CompletedBy);
        Assert.Equal(SceneLog(t)[^2].EventId, task.CompletedEventId);
        Assert.Equal("selected", SceneLog(t)[^2].Kind);
    }

    [Fact]
    public async Task S35d_the_task_stays_active_until_every_predicate_is_true()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right"), Obj("cup_1", "left")));
        t.Engine.MarkRoundStart();
        var first = await t.CallAsync("move_object", MoveJson("apple_1", "arrange", region: "right"));
        Assert.Null(first.Data);
        Assert.Equal("t1", t.Engine.GetActiveTask()!.TaskId);
        var second = await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "left"));
        Assert.Equal("completed", Data(second).GetProperty("task_status").GetString());
    }

    [Fact]
    public async Task S35d_a_spawn_can_complete_a_quantity_task()
    {
        await using var t = await StartAsync(("pen", "right"));
        var quantity = """{"type":"quantity","selector":{"catalog_id":"cup"},"region":"right","count":2}""";
        // feasibility needs two cups in the scene already
        await t.CallAsync("spawn_objects", SpawnJson(("cup", "left", 2)));
        t.Engine.MarkRoundStart();
        await StartTaskAsync(t, Predicates(quantity));
        Assert.True((await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "right"))).IsSuccess);
        var last = await t.CallAsync("move_object", MoveJson("cup_2", "arrange", region: "right"));
        Assert.Equal("completed", Data(last).GetProperty("task_status").GetString());
    }

    [Fact]
    public async Task S35d_a_removal_can_complete_an_lte_quantity_task()
    {
        await using var t = await StartAsync(("apple", "right"), ("apple", "right"), ("pen", "left"));
        var quantity = """{"type":"quantity","selector":{"catalog_id":"apple"},"region":"right","count":1,"comparison":"lte"}""";
        await StartTaskAsync(t, Predicates(quantity));
        t.Engine.MarkRoundStart();
        var r = await t.CallAsync("remove_objects", Ids("apple_2"));
        Assert.Equal("completed", Data(r).GetProperty("task_status").GetString());
    }

    [Fact]
    public async Task S35d_predicates_through_the_tool_cover_left_of_and_contained_in()
    {
        await using var t = await SceneAsync();
        await StartTaskAsync(t, Predicates("""{"type":"left_of","object_id":"cup_1","reference_id":"apple_1"}""", """{"type":"contained_in","object_id":"pen_1","container_id":"bag_1"}"""));
        t.Engine.MarkRoundStart();
        await t.CallAsync("move_object", MoveJson("pen_1", "arrange", container: "bag_1"));
        Assert.NotNull(t.Engine.GetActiveTask());
        await t.CallAsync("move_object", MoveJson("apple_1", "arrange", region: "right"));
        Assert.Null(t.Engine.GetActiveTask());
        Assert.Equal(TaskStatuses.Completed, History(t).Single().Status);
    }

    [Fact]
    public async Task S35d_unchanged_and_before_use_the_events_since_the_task_started()
    {
        await using var t = await SceneAsync();
        // pen_1 and bag_1 move before the task starts and do not count
        t.Engine.PlayerMove("pen_1", "left", null);
        t.Engine.BeginTurn("n");
        var predicates = Predicates("""{"type":"unchanged","object_id":"apple_1"}""", """{"type":"before","object_id":"cup_1","reference_id":"pen_1"}""");
        await StartTaskAsync(t, predicates);
        t.Engine.MarkRoundStart();
        await t.CallAsync("move_object", MoveJson("cup_1", "arrange", region: "left"));
        Assert.NotNull(t.Engine.GetActiveTask());
        await t.CallAsync("move_object", MoveJson("pen_1", "arrange", region: "right"));
        Assert.Null(t.Engine.GetActiveTask());
        Assert.Equal(TaskStatuses.Completed, History(t).Single().Status);
    }

    [Fact]
    public async Task S35d_unchanged_fails_when_the_object_moves()
    {
        await using var t = await SceneAsync();
        var predicates = Predicates("""{"type":"unchanged","object_id":"apple_1"}""", Selected("cup_1"));
        await StartTaskAsync(t, predicates);
        t.Engine.MarkRoundStart();
        await t.CallAsync("move_object", MoveJson("apple_1", "arrange", region: "right"));
        t.Engine.PlayerSelect("cup_1");
        Assert.Equal("t1", t.Engine.GetActiveTask()!.TaskId);
    }
}

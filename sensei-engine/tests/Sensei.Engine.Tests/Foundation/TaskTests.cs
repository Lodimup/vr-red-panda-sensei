using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>Task model, validation (S35a/b, S104, S106), evaluation (S35d), lifecycle (S36, S96, S104, S105, S107, S108).</summary>
public sealed class TaskTests
{
    // ---------------------------------------------------------------- helpers

    private static TaskPredicate P(string type, string? obj = null, string? region = null, string? container = null, string? reference = null,
        PredicateSelector? selector = null, int? count = null, string? comparison = null) => new()
    {
        Type = type, ObjectId = obj, Region = region, ContainerId = container, ReferenceId = reference, Selector = selector, Count = count, Comparison = comparison,
    };

    private static PredicateSelector Sel(string catalogId, string? variant = null, string? propName = null, string? propValue = null) => new()
    {
        CatalogId = catalogId, Variant = variant, Property = propName is null ? null : new SelectorProperty { Name = propName, Value = propValue! },
    };

    private static ToolFailure? Validate(TestEngine t, IReadOnlyList<TaskPredicate> predicates, IReadOnlyList<ScenarioProperty>? scenario = null)
    {
        lock (t.Core.Sync) return t.Core.Tasks.ValidateStart(predicates, scenario ?? Array.Empty<ScenarioProperty>());
    }

    private static TaskView Start(TestEngine t, params TaskPredicate[] predicates) => StartWith(t, predicates, Array.Empty<ScenarioProperty>());

    private static TaskView StartWith(TestEngine t, IReadOnlyList<TaskPredicate> predicates, IReadOnlyList<ScenarioProperty> scenario)
    {
        TaskView view;
        lock (t.Core.Sync)
        {
            var failure = t.Core.Tasks.ValidateStart(predicates, scenario);
            Assert.Null(failure);
            view = t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "Put the apple on the left." },
                new[] { new SkillTarget { Dimension = SkillDimensions.Listening, Key = "ringo", Japanese = "りんご" } },
                predicates, scenario, null, false, t.Core.CurrentTurnId);
        }
        t.Core.FlushNotifications();
        return view;
    }

    private static string? Status(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.Tasks.History.LastOrDefault()?.Status;
    }

    // ---------------------------------------------------------------- S35a shape, S21 predicate fields

    [Fact]
    public void S35a_both_instruction_languages_absent_is_invalid()
    {
        Assert.NotNull(TaskValidator.ValidateShape(new TaskInstruction(), new[] { P("selected", "apple_1") }));
        Assert.Null(TaskValidator.ValidateShape(new TaskInstruction { Japanese = "りんご" }, new[] { P("selected", "apple_1") }));
        Assert.Null(TaskValidator.ValidateShape(new TaskInstruction { English = "apple" }, new[] { P("selected", "apple_1") }));
    }

    [Fact]
    public void S36_revise_shape_does_not_require_an_instruction()
    {
        Assert.Null(TaskValidator.ValidateShape(null, null, instructionRequired: false));
        Assert.NotNull(TaskValidator.ValidateShape(null, null));
    }

    public static TheoryData<TaskPredicate, string> InvalidShapes => new()
    {
        { P("selected"), "object_id" },
        { P("selected", "a_1", region: "left"), "region" },
        { P("in_region", region: "left"), "object_id or selector" },
        { P("in_region", "a_1", selector: Sel("apple"), region: "left"), "object_id or selector" },
        { P("in_region", "a_1"), "region" },
        { P("in_region", "a_1", region: "left", count: 2), "count" },
        { P("contained_in", "a_1"), "container_id" },
        { P("contained_in", "a_1", container: "bag_1", region: "left"), "region" },
        { P("quantity", region: "left", count: 1), "selector" },
        { P("quantity", selector: Sel("apple"), count: 1), "region or container_id" },
        { P("quantity", selector: Sel("apple"), region: "left", container: "bag_1", count: 1), "region or container_id" },
        { P("quantity", selector: Sel("apple"), region: "left"), "count" },
        { P("quantity", "a_1", selector: Sel("apple"), region: "left", count: 1), "object_id" },
        { P("quantity", selector: Sel("apple"), region: "left", count: 9), "count" },
        { P("quantity", selector: Sel("apple"), region: "left", count: 0), "count" },
        { P("quantity", selector: Sel("apple"), region: "left", count: 1, comparison: "gt"), "comparison" },
        { P("left_of", "a_1"), "reference_id" },
        { P("right_of", reference: "a_1"), "object_id" },
        { P("before", "a_1", "left", reference: "b_1"), "region" },
        { P("unchanged", "a_1", reference: "b_1"), "reference_id" },
        { P("in_region", "a_1", region: "kitchen"), "region" },
        { P("touching", "a_1"), "type" },
    };

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public void S21_predicate_fields_are_decided_by_type(TaskPredicate predicate, string expectedInDetail)
    {
        var detail = TaskValidator.ValidateShape(new TaskInstruction { English = "x" }, new[] { predicate });
        Assert.NotNull(detail);
        Assert.Contains(expectedInDetail, detail);
        Assert.StartsWith("predicates[0]", detail);
    }

    public static TheoryData<TaskPredicate> ValidShapes => new()
    {
        P("selected", "a_1"),
        P("in_region", "a_1", region: "left"),
        P("in_region", selector: Sel("apple"), region: "learner_hand"),
        P("contained_in", "a_1", container: "bag_1"),
        P("contained_in", selector: Sel("apple"), container: "bag_1"),
        P("quantity", selector: Sel("apple"), region: "left", count: 1),
        P("quantity", selector: Sel("apple"), container: "bag_1", count: 8, comparison: "lte"),
        P("left_of", "a_1", reference: "b_1"),
        P("right_of", "a_1", reference: "b_1"),
        P("unchanged", "a_1"),
        P("before", "a_1", reference: "b_1"),
    };

    [Theory]
    [MemberData(nameof(ValidShapes))]
    public void S21_valid_predicate_shapes_pass(TaskPredicate predicate) =>
        Assert.Null(TaskValidator.ValidateShape(new TaskInstruction { English = "x" }, new[] { predicate }));

    // ---------------------------------------------------------------- S35b, S104, S106 feasibility

    [Fact]
    public async Task S35a_a_second_task_while_one_is_active_is_task_already_active()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        Start(t, P("in_region", a, region: "left"));
        Assert.Equal(ToolErrorCodes.TaskAlreadyActive, Validate(t, new[] { P("in_region", a, region: "right") })!.Code);
    }

    [Theory]
    [InlineData("object_id")]
    [InlineData("reference_id")]
    [InlineData("container_id")]
    public async Task S35b_a_missing_referenced_object_is_impossible(string field)
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple", SceneRegions.Left);
        var bag = Spawn(t, "bag");
        var p = field switch
        {
            "object_id" => P("in_region", "ghost_1", region: "right"),
            "reference_id" => P("left_of", a, reference: "ghost_1"),
            _ => P("contained_in", a, container: "ghost_1"),
        };
        var f = Validate(t, new[] { p });
        Assert.Equal(ToolErrorCodes.ImpossibleTask, f!.Code);
        Assert.Contains("ghost_1", f.Detail);
        _ = bag;
    }

    [Fact]
    public async Task S35b_container_id_that_is_not_a_container_is_impossible()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var cup = Spawn(t, "cup");
        Assert.Contains("not a container", Validate(t, new[] { P("contained_in", a, container: cup) })!.Detail);
    }

    [Fact]
    public async Task S35b_object_id_equal_to_reference_id_is_impossible()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        Assert.Equal(ToolErrorCodes.ImpossibleTask, Validate(t, new[] { P("left_of", a, reference: a) })!.Code);
    }

    [Theory]
    [InlineData("pizza", null, null, "catalog")]
    [InlineData("cup", "green", null, "variant")]
    [InlineData("apple", null, "owner", "property")]
    public async Task S35b_selector_must_name_catalog_variant_and_declared_property(string catalogId, string? variant, string? prop, string expected)
    {
        await using var t = await TestEngine.CreateAsync();
        Spawn(t, ("apple", "center"), ("cup", "center"));
        var f = Validate(t, new[] { P("in_region", selector: Sel(catalogId, variant, prop, prop is null ? null : "mine"), region: "left") });
        Assert.Equal(ToolErrorCodes.ImpossibleTask, f!.Code);
        Assert.Contains(expected, f.Detail);
    }

    [Fact]
    public async Task S35b_a_declared_selector_property_is_accepted()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var scenario = new[] { new ScenarioProperty { ObjectId = a, Name = "owner", Value = "mine" } };
        Assert.Null(Validate(t, new[] { P("in_region", selector: Sel("apple", null, "owner", "mine"), region: "left") }, scenario));
    }

    [Theory]
    [InlineData("eq", 3, true)]
    [InlineData("eq", 2, false)]
    [InlineData("gte", 3, true)]
    [InlineData("gte", 2, false)]
    public async Task S35b_quantity_cannot_exceed_matching_objects_in_the_scene(string comparison, int count, bool impossible)
    {
        await using var t = await TestEngine.CreateAsync();
        Spawn(t, ("apple", "center"), ("apple", "center"));
        var f = Validate(t, new[] { P("quantity", selector: Sel("apple"), region: "left", count: count, comparison: comparison) });
        if (impossible) Assert.Contains("count", f!.Detail);
        else Assert.Null(f);
    }

    [Fact]
    public async Task S35b_lte_quantity_is_never_impossible_by_count()
    {
        await using var t = await TestEngine.CreateAsync();
        var apples = Spawn(t, ("apple", "center"), ("apple", "center"));
        Assert.Null(Validate(t, new[] { P("quantity", selector: Sel("apple"), region: "center", count: 8, comparison: "lte"), P("in_region", apples[0], region: "left") }));
    }

    [Fact]
    public async Task S35b_a_task_already_true_at_start_is_impossible()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple", SceneRegions.Left);
        var f = Validate(t, new[] { P("in_region", a, region: "left"), P("unchanged", a) });
        Assert.Equal(ToolErrorCodes.ImpossibleTask, f!.Code);
        Assert.Contains("already true", f.Detail);
        Assert.Null(Validate(t, new[] { P("in_region", a, region: "right"), P("unchanged", a) }));
    }

    [Fact]
    public async Task S35b_scenario_property_object_must_exist()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var f = Validate(t, new[] { P("in_region", a, region: "left") }, new[] { new ScenarioProperty { ObjectId = "ghost_1", Name = "owner", Value = "x" } });
        Assert.Contains("scenario_properties[0]", f!.Detail);
    }

    [Fact]
    public async Task S104_a_task_of_only_unchanged_predicates_is_impossible()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var f = Validate(t, new[] { P("unchanged", a) });
        Assert.Equal(ToolErrorCodes.ImpossibleTask, f!.Code);
        Assert.Contains("not unchanged", f.Detail);
    }

    [Fact]
    public async Task S106_one_object_required_in_two_places_is_impossible()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var bag = Spawn(t, "bag");
        Assert.Contains("two different places", Validate(t, new[] { P("in_region", a, region: "left"), P("in_region", a, region: "right") })!.Detail);
        Assert.Contains("two different places", Validate(t, new[] { P("in_region", a, region: "left"), P("contained_in", a, container: bag) })!.Detail);
    }

    [Fact]
    public async Task S106_contained_in_for_a_container_is_impossible()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag");
        var box = Spawn(t, "box");
        Assert.Contains("is a container", Validate(t, new[] { P("contained_in", bag, container: box) })!.Detail);
        Assert.Contains("is a container", Validate(t, new[] { P("contained_in", selector: Sel("bag"), container: box) })!.Detail);
    }

    [Fact]
    public async Task S106_contradictory_order_on_one_pair_is_impossible()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple", SceneRegions.Left);
        var b = Spawn(t, "banana", SceneRegions.Left);
        Assert.Contains("contradicts", Validate(t, new[] { P("left_of", a, reference: b), P("right_of", a, reference: b) })!.Detail);
        Assert.Contains("contradicts", Validate(t, new[] { P("left_of", a, reference: b), P("left_of", b, reference: a) })!.Detail);
        Assert.Contains("contradicts", Validate(t, new[] { P("before", a, reference: b), P("before", b, reference: a) })!.Detail);
        Assert.Null(Validate(t, new[] { P("right_of", a, reference: b), P("left_of", b, reference: a) }));
    }

    [Fact]
    public async Task S106_a_positional_predicate_on_an_object_inside_a_container_is_impossible()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag");
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana", SceneRegions.Right);
        SenseiMove(t, a, null, bag);
        Assert.Contains("inside a container", Validate(t, new[] { P("left_of", a, reference: b) })!.Detail);
        Assert.Contains("inside a container", Validate(t, new[] { P("right_of", b, reference: a) })!.Detail);
    }

    // ---------------------------------------------------------------- S35c, S104 start

    [Fact]
    public async Task S35c_start_creates_t1_active_with_baseline_and_a_task_started_event()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var view = Start(t, P("in_region", a, region: "left"));
        Assert.Equal("t1", view.TaskId);
        Assert.Equal(TaskStatuses.Active, view.Status);
        Assert.Equal(1, view.SourceRevision);
        var started = t.Core.World.Events.Last();
        Assert.Equal(SceneEventKinds.TaskStarted, started.Kind);
        Assert.Equal("t1", started.TaskId);
        Assert.Equal(started.EventId, view.StartEventId);
        Assert.Equal(1, started.Revision);
        Assert.Equal(1, t.Engine.GetScene().Revision);
        Assert.True(view.AssistanceBaseline.ShowRomaji);
        Assert.Equal("t1", t.Engine.GetActiveTask()!.TaskId);
        Assert.Equal("t1", t.Events.Tasks.Single().TaskId);
    }

    // ---------------------------------------------------------------- S35d evaluation, each predicate type

    [Fact]
    public async Task S35d_selected_completes_on_player_select()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana");
        Start(t, P("selected", a));
        t.Engine.PlayerSelect(b);
        Assert.Equal(TaskStatuses.Active, Status(t));
        t.Engine.PlayerSelect(a);
        Assert.Equal(TaskStatuses.Completed, Status(t));
        var task = t.Core.Tasks.History.Single();
        Assert.Equal(SceneActors.Player, task.CompletedBy);
        Assert.Equal(SceneEventKinds.Selected, t.Core.World.FindEvent(task.CompletedEventId!.Value)!.Kind);
    }

    [Fact]
    public async Task S35d_in_region_by_object_requires_region_and_no_container()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var bag = Spawn(t, "bag", SceneRegions.Left);
        Start(t, P("in_region", a, region: "left"));
        t.Engine.PlayerMove(a, null, bag);
        Assert.Equal(TaskStatuses.Active, Status(t));
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Fact]
    public async Task S35d_in_region_by_selector_needs_one_match()
    {
        await using var t = await TestEngine.CreateAsync();
        var cups = Spawn(t, ("cup", "center"), ("cup", "center"));
        Start(t, P("in_region", selector: Sel("cup", "blue"), region: "right"));
        t.Engine.PlayerMove(cups[1], SceneRegions.Right, null);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Fact]
    public async Task S35d_selector_variant_must_match()
    {
        await using var t = await TestEngine.CreateAsync();
        var cup = Spawn(t, "cup");
        Start(t, P("in_region", selector: Sel("cup", "red"), region: "right"));
        t.Engine.PlayerMove(cup, SceneRegions.Right, null);
        Assert.Equal(TaskStatuses.Active, Status(t));
    }

    [Fact]
    public async Task S35d_selector_property_matches_only_the_declared_object()
    {
        await using var t = await TestEngine.CreateAsync();
        var apples = Spawn(t, ("apple", "center"), ("apple", "center"));
        var scenario = new[] { new ScenarioProperty { ObjectId = apples[1], Name = "owner", Value = "sensei" } };
        StartWith(t, new[] { P("in_region", selector: Sel("apple", null, "owner", "sensei"), region: "left") }, scenario);
        t.Engine.PlayerMove(apples[0], SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Active, Status(t));
        t.Engine.PlayerMove(apples[1], SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Fact]
    public async Task S35d_contained_in_by_object_and_selector()
    {
        await using var t = await TestEngine.CreateAsync();
        var bag = Spawn(t, "bag");
        var key = Spawn(t, "key");
        var pen = Spawn(t, "pen");
        Start(t, P("contained_in", key, container: bag), P("contained_in", selector: Sel("pen"), container: bag));
        t.Engine.PlayerMove(key, null, bag);
        Assert.Equal(TaskStatuses.Active, Status(t));
        t.Engine.PlayerMove(pen, null, bag);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Theory]
    [InlineData("eq", 2, new[] { 1, 2, 3 }, 2)]
    [InlineData("gte", 2, new[] { 1, 2 }, 2)]
    [InlineData("lte", 1, new[] { 1 }, -1)]
    public async Task S35d_quantity_compares_matches_in_a_region(string comparison, int count, int[] movesToLeft, int completesAfterMove)
    {
        await using var t = await TestEngine.CreateAsync();
        var apples = Spawn(t, ("apple", "left"), ("apple", "left"), ("apple", "left"), ("apple", "center"));
        // start with three on the left, then move them right one by one for lte, or build up from zero otherwise
        if (comparison == "lte")
        {
            Start(t, P("quantity", selector: Sel("apple"), region: "left", count: count, comparison: comparison));
            t.Engine.PlayerMove(apples[0], SceneRegions.Right, null);
            Assert.Equal(TaskStatuses.Active, Status(t));
            t.Engine.PlayerMove(apples[1], SceneRegions.Right, null);
            Assert.Equal(TaskStatuses.Completed, Status(t));
            return;
        }
        foreach (var id in apples.Take(3)) t.Engine.PlayerMove(id, SceneRegions.Right, null);
        Start(t, P("quantity", selector: Sel("apple"), region: "left", count: count, comparison: comparison));
        for (var i = 0; i < movesToLeft.Length; i++)
        {
            t.Engine.PlayerMove(apples[i], SceneRegions.Left, null);
            var expected = i + 1 >= completesAfterMove ? TaskStatuses.Completed : TaskStatuses.Active;
            Assert.Equal(expected, Status(t));
            if (expected == TaskStatuses.Completed) break;
        }
    }

    [Fact]
    public async Task S35d_quantity_eq_is_false_when_there_are_too_many()
    {
        await using var t = await TestEngine.CreateAsync();
        var apples = Spawn(t, ("apple", "center"), ("apple", "center"), ("apple", "center"));
        var view = Start(t, P("quantity", selector: Sel("apple"), region: "left", count: 1));
        Assert.Equal(TaskStatuses.Active, view.Status);
        lock (t.Core.Sync)
        {
            t.Core.World.CommitMove(apples[0], "left", null, SceneActors.Sensei, "arrange", 0);
            Assert.Equal(TaskStatuses.Completed, t.Core.Tasks.History.Single().Status);
        }
    }

    [Fact]
    public async Task S35d_quantity_in_a_container_counts_contents()
    {
        await using var t = await TestEngine.CreateAsync();
        var box = Spawn(t, "box");
        var pens = Spawn(t, ("pen", "center"), ("pen", "center"));
        Start(t, P("quantity", selector: Sel("pen"), container: box, count: 2));
        t.Engine.PlayerMove(pens[0], null, box);
        Assert.Equal(TaskStatuses.Active, Status(t));
        t.Engine.PlayerMove(pens[1], null, box);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Fact]
    public async Task S35d_quantity_in_a_region_ignores_objects_inside_containers()
    {
        await using var t = await TestEngine.CreateAsync();
        var box = Spawn(t, "box", SceneRegions.Left);
        var pens = Spawn(t, ("pen", "center"), ("pen", "center"));
        Start(t, P("quantity", selector: Sel("pen"), region: "left", count: 1));
        t.Engine.PlayerMove(pens[0], null, box);
        Assert.Equal(TaskStatuses.Active, Status(t));
    }

    [Fact]
    public async Task S35d_left_of_compares_region_then_slot()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple", SceneRegions.Right);
        var b = Spawn(t, "banana", SceneRegions.Center);
        Start(t, P("left_of", a, reference: b));
        t.Engine.PlayerMove(a, SceneRegions.Center, null); // same region, a has the higher slot
        Assert.Equal(TaskStatuses.Active, Status(t));
        t.Engine.PlayerMove(b, SceneRegions.Right, null);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Fact]
    public async Task S35d_right_of_within_one_region_uses_slots()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple", SceneRegions.Left);
        var b = Spawn(t, "banana", SceneRegions.Left);
        Start(t, P("right_of", a, reference: b));
        t.Engine.PlayerMove(a, SceneRegions.Center, null);
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Fact]
    public async Task S35d_left_of_is_false_outside_the_practice_area()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple", SceneRegions.Right);
        var b = Spawn(t, "banana", SceneRegions.Center);
        Start(t, P("left_of", a, reference: b), P("selected", b));
        t.Engine.PlayerMove(a, SceneRegions.LearnerHand, null);
        t.Engine.PlayerSelect(b);
        Assert.Equal(TaskStatuses.Active, Status(t));
    }

    [Fact]
    public async Task S35d_unchanged_turns_false_after_a_move_of_that_object()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana");
        Start(t, P("unchanged", a), P("in_region", b, region: "left"));
        t.Engine.PlayerMove(a, SceneRegions.Right, null);
        t.Engine.PlayerMove(b, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Active, Status(t));
    }

    [Fact]
    public async Task S35d_unchanged_holds_while_the_object_stays()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana");
        Start(t, P("unchanged", a), P("in_region", b, region: "left"));
        t.Engine.PlayerMove(b, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Fact]
    public async Task S35d_before_needs_both_moved_with_the_object_first()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana");
        Start(t, P("before", a, reference: b));
        t.Engine.PlayerMove(b, SceneRegions.Left, null);
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Active, Status(t));
    }

    [Fact]
    public async Task S35d_before_completes_in_the_right_order()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana");
        Start(t, P("before", a, reference: b));
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Active, Status(t));
        t.Engine.PlayerMove(b, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Fact]
    public async Task S35d_completion_appends_task_completed_without_revision_and_raises_task_changed()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        Start(t, P("in_region", a, region: "left"));
        t.Events.Clear();
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        var events = t.Core.World.Events;
        var moved = events[^2];
        var completed = events[^1];
        Assert.Equal(SceneEventKinds.Moved, moved.Kind);
        Assert.Equal(SceneEventKinds.TaskCompleted, completed.Kind);
        Assert.Equal(moved.Revision, completed.Revision);
        Assert.Equal(SceneActors.Player, completed.Actor);
        var task = t.Core.Tasks.History.Single();
        Assert.Equal(moved.EventId, task.CompletedEventId);
        Assert.Equal(TaskStatuses.Completed, t.Events.Tasks.Single().Status);
        Assert.Null(t.Engine.GetActiveTask());
        Assert.Equal(new[] { "moved", "task_completed" }, t.Events.SceneEvents.Select(e => e.Kind));
    }

    [Fact]
    public async Task S35d_sensei_completion_marks_the_tool_result_task_status()
    {
        var mutate = new MutatingProbeTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { mutate });
        Spawn(t, ("apple", "center"), ("apple", "left"));
        Start(t, P("quantity", selector: Sel("apple"), region: "center", count: 2));
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("test_mutate", """{"action":"spawn"}""");
        Assert.Equal("completed", r.Data!.Value.GetProperty("task_status").GetString());
        Assert.Equal(SceneActors.Sensei, t.Core.Tasks.History.Single().CompletedBy);
        var other = await t.CallAsync("test_mutate", """{"action":"spawn"}""");
        Assert.Equal(System.Text.Json.JsonValueKind.Undefined, other.Data?.ValueKind ?? System.Text.Json.JsonValueKind.Undefined);
    }

    // ---------------------------------------------------------------- S36, S104, S108 revise / abandon / end

    [Fact]
    public async Task S36_update_with_no_active_task_or_wrong_id_is_no_active_task()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        lock (t.Core.Sync) Assert.Equal(ToolErrorCodes.NoActiveTask, t.Core.Tasks.ValidateUpdate("t1", "abandon", null, null)!.Code);
        Start(t, P("in_region", a, region: "left"));
        lock (t.Core.Sync)
        {
            Assert.Equal(ToolErrorCodes.NoActiveTask, t.Core.Tasks.ValidateUpdate("t9", "abandon", null, null)!.Code);
            Assert.Null(t.Core.Tasks.ValidateUpdate("t1", "abandon", null, null));
            Assert.Equal(ToolErrorCodes.InvalidArguments, t.Core.Tasks.ValidateUpdate("t1", "revise", null, null)!.Code);
            Assert.Null(t.Core.Tasks.ValidateUpdate("t1", "revise", new TaskInstruction { English = "x" }, null));
            Assert.Equal(ToolErrorCodes.ImpossibleTask, t.Core.Tasks.ValidateUpdate("t1", "revise", null, new[] { P("in_region", "ghost_1", region: "left") })!.Code);
        }
    }

    [Fact]
    public async Task S108_predicate_revise_keeps_identity_and_boundary_and_resets_predicate_window()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana");
        var start = Start(t, P("in_region", a, region: "left"));
        t.Engine.PlayerMove(b, SceneRegions.Right, null);
        TaskView revised;
        lock (t.Core.Sync) revised = t.Core.Tasks.CommitRevise(null, new[] { P("in_region", b, region: "left"), P("unchanged", a) }, t.Core.CurrentTurnId);
        Assert.Equal("t1", revised.TaskId);
        Assert.Equal(start.StartEventId, revised.StartEventId);
        Assert.Equal(start.AssistanceBaseline.ShowRomaji, revised.AssistanceBaseline.ShowRomaji);
        Assert.Equal(start.IsFreshVariation, revised.IsFreshVariation);
        var revisedEvent = t.Core.World.Events.Last();
        Assert.Equal(SceneEventKinds.TaskRevised, revisedEvent.Kind);
        Assert.Equal(revisedEvent.EventId, revised.PredicatesSinceEventId);
        Assert.Equal(t.Engine.GetScene().Revision, revised.SourceRevision);
        Assert.Equal(revisedEvent.Revision, t.Engine.GetScene().Revision);
    }

    [Fact]
    public async Task S108_instruction_only_revise_changes_nothing_else()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var start = Start(t, P("in_region", a, region: "left"));
        Spawn(t, "pen");
        TaskView revised;
        lock (t.Core.Sync) revised = t.Core.Tasks.CommitRevise(new TaskInstruction { Japanese = "りんごを左に" }, null, 0);
        Assert.Equal("りんごを左に", revised.Instruction.Japanese);
        Assert.Null(revised.Instruction.English);
        Assert.Equal(start.PredicatesSinceEventId, revised.PredicatesSinceEventId);
        Assert.Equal(start.SourceRevision, revised.SourceRevision);
        Assert.Single(revised.Predicates);
    }

    [Fact]
    public async Task S104_revise_is_evaluated_at_once()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana", SceneRegions.Left);
        Start(t, P("in_region", a, region: "left"));
        lock (t.Core.Sync) t.Core.Tasks.CommitRevise(null, new[] { P("in_region", b, region: "left") }, 0);
        Assert.Equal(TaskStatuses.Completed, Status(t));
        Assert.Equal(SceneActors.Sensei, t.Core.Tasks.History.Single().CompletedBy);
    }

    [Fact]
    public async Task S108_unchanged_after_a_predicate_revise_ignores_earlier_moves()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana");
        Start(t, P("in_region", b, region: "right"));
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        lock (t.Core.Sync) t.Core.Tasks.CommitRevise(null, new[] { P("unchanged", a), P("in_region", b, region: "left") }, 0);
        t.Engine.PlayerMove(b, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Completed, Status(t));
    }

    [Fact]
    public async Task S104_abandon_and_end_append_events_without_revision()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        Start(t, P("in_region", a, region: "left"));
        TaskView abandoned;
        lock (t.Core.Sync) abandoned = t.Core.Tasks.CommitAbandon(0);
        Assert.Equal(TaskStatuses.Abandoned, abandoned.Status);
        Assert.Equal(SceneEventKinds.TaskAbandoned, t.Core.World.Events.Last().Kind);
        Assert.Null(t.Engine.GetActiveTask());
        Start(t, P("in_region", a, region: "right"));
        Assert.Equal("t2", t.Engine.GetActiveTask()!.TaskId);
        lock (t.Core.Sync) Assert.Equal(TaskStatuses.Ended, t.Core.Tasks.CommitEnd(0)!.Status);
        Assert.Equal(SceneEventKinds.TaskEnded, t.Core.World.Events.Last().Kind);
        lock (t.Core.Sync) Assert.Null(t.Core.Tasks.CommitEnd(0));
        t.Core.FlushNotifications();
        Assert.Equal(1, t.Engine.GetScene().Revision);
        Assert.Equal(4, t.Events.Tasks.Count);
    }

    [Fact]
    public async Task S104_a_closed_task_is_no_longer_evaluated()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        Start(t, P("in_region", a, region: "left"));
        lock (t.Core.Sync) t.Core.Tasks.CommitAbandon(0);
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        Assert.Equal(TaskStatuses.Abandoned, Status(t));
    }

    // ---------------------------------------------------------------- S32a / S107 removal, S33 references

    [Fact]
    public async Task S32a_removing_an_object_named_by_a_predicate_is_object_in_active_task()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var b = Spawn(t, "banana", SceneRegions.Right);
        var bag = Spawn(t, "bag");
        var pen = Spawn(t, "pen");
        Start(t, P("left_of", a, reference: b), P("contained_in", pen, container: bag));
        lock (t.Core.Sync)
        {
            foreach (var id in new[] { a, b, bag, pen }) Assert.Equal(ToolErrorCodes.ObjectInActiveTask, t.Core.Tasks.CheckRemoval(new[] { id })!.Code);
        }
    }

    [Fact]
    public async Task S107_removal_that_leaves_too_few_selector_matches_is_object_in_active_task()
    {
        await using var t = await TestEngine.CreateAsync();
        var apples = Spawn(t, ("apple", "center"), ("apple", "center"), ("apple", "center"));
        Start(t, P("quantity", selector: Sel("apple"), region: "left", count: 2, comparison: "gte"));
        lock (t.Core.Sync)
        {
            Assert.Null(t.Core.Tasks.CheckRemoval(new[] { apples[0] }));
            Assert.Equal(ToolErrorCodes.ObjectInActiveTask, t.Core.Tasks.CheckRemoval(new[] { apples[0], apples[1] })!.Code);
        }
    }

    [Fact]
    public async Task S107_lte_quantity_never_blocks_removal()
    {
        await using var t = await TestEngine.CreateAsync();
        var apples = Spawn(t, ("apple", "left"), ("apple", "left"), ("apple", "left"));
        Start(t, P("quantity", selector: Sel("apple"), region: "left", count: 1, comparison: "lte"));
        lock (t.Core.Sync) Assert.Null(t.Core.Tasks.CheckRemoval(apples));
    }

    [Fact]
    public async Task S107_removing_the_last_selector_match_of_in_region_is_blocked()
    {
        await using var t = await TestEngine.CreateAsync();
        var cups = Spawn(t, ("cup", "center"), ("cup", "center"));
        Start(t, P("in_region", selector: Sel("cup"), region: "left"));
        lock (t.Core.Sync)
        {
            Assert.Null(t.Core.Tasks.CheckRemoval(new[] { cups[0] }));
            Assert.NotNull(t.Core.Tasks.CheckRemoval(cups));
        }
    }

    [Fact]
    public async Task S107_removing_an_object_with_a_scenario_property_is_blocked()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var pen = Spawn(t, "pen");
        StartWith(t, new[] { P("in_region", a, region: "left") }, new[] { new ScenarioProperty { ObjectId = pen, Name = "owner", Value = "sensei" } });
        lock (t.Core.Sync) Assert.Equal(ToolErrorCodes.ObjectInActiveTask, t.Core.Tasks.CheckRemoval(new[] { pen })!.Code);
    }

    [Fact]
    public async Task S32a_without_an_active_task_removal_is_allowed()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        lock (t.Core.Sync) Assert.Null(t.Core.Tasks.CheckRemoval(new[] { a }));
    }

    [Fact]
    public async Task S33_references_count_directly_or_through_a_selector()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        var cup = Spawn(t, "cup");
        var pen = Spawn(t, "pen");
        Start(t, P("in_region", a, region: "left"), P("in_region", selector: Sel("cup"), region: "right"));
        lock (t.Core.Sync)
        {
            Assert.True(t.Core.Tasks.IsReferencedByActiveTask(a));
            Assert.True(t.Core.Tasks.IsReferencedByActiveTask(cup));
            Assert.False(t.Core.Tasks.IsReferencedByActiveTask(pen));
        }
    }

    // ---------------------------------------------------------------- S96 binding

    [Fact]
    public async Task S96_binding_prefers_named_then_active_then_recently_closed()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        t.Engine.BeginTurn("turn one");
        Start(t, P("in_region", a, region: "left"));
        lock (t.Core.Sync)
        {
            Assert.Equal("t1", t.Core.Tasks.FindBindable(null, t.Core.CurrentTurnId, out var found)!.TaskId);
            Assert.True(found);
        }
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        t.Engine.BeginTurn("turn two");
        lock (t.Core.Sync)
        {
            Assert.Equal("t1", t.Core.Tasks.FindBindable(null, t.Core.CurrentTurnId, out _)!.TaskId);
            Assert.Equal("t1", t.Core.Tasks.FindBindable("t1", t.Core.CurrentTurnId, out var named)!.TaskId);
            Assert.True(named);
            Assert.Null(t.Core.Tasks.FindBindable("t7", t.Core.CurrentTurnId, out var missing));
            Assert.False(missing);
        }
        t.Engine.BeginTurn("turn three");
        lock (t.Core.Sync)
        {
            Assert.Null(t.Core.Tasks.FindBindable(null, t.Core.CurrentTurnId, out var none));
            Assert.True(none);
            Assert.Null(t.Core.Tasks.FindBindable("t1", t.Core.CurrentTurnId, out var tooOld));
            Assert.False(tooOld);
        }
    }

    [Fact]
    public async Task S96_an_abandoned_task_is_never_bound()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        t.Engine.BeginTurn("x");
        Start(t, P("in_region", a, region: "left"));
        lock (t.Core.Sync)
        {
            t.Core.Tasks.CommitAbandon(t.Core.CurrentTurnId);
            Assert.Null(t.Core.Tasks.FindBindable(null, t.Core.CurrentTurnId, out _));
            Assert.Null(t.Core.Tasks.FindBindable("t1", t.Core.CurrentTurnId, out var found));
            Assert.False(found);
        }
    }

    [Fact]
    public async Task S96_get_active_task_returns_only_an_active_task()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        Start(t, P("in_region", a, region: "left"));
        Assert.NotNull(t.Engine.GetActiveTask());
        t.Engine.PlayerMove(a, SceneRegions.Left, null);
        Assert.Null(t.Engine.GetActiveTask());
        Assert.Null(t.Engine.GetScene().ActiveTask);
    }

    [Fact]
    public async Task S35c_task_views_handed_out_are_copies()
    {
        await using var t = await TestEngine.CreateAsync();
        var a = Spawn(t, "apple");
        Start(t, P("in_region", a, region: "left"));
        var copy = t.Engine.GetActiveTask()!;
        copy.Status = "completed";
        copy.Predicates.Clear();
        Assert.Equal(TaskStatuses.Active, t.Engine.GetActiveTask()!.Status);
        Assert.Single(t.Engine.GetActiveTask()!.Predicates);
    }
}

using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>
/// Review round 2, findings 3 and 4 at the cause: a call's result agrees with what it left in the world (S22), and work of an
/// ended generation settles <c>cancelled</c> whether its body returns or faults (S27, S87).
/// </summary>
public sealed class CallDispositionTests
{
    public enum Body
    {
        /// <summary>Commits under <c>Locked</c> (a mutating tool), then waits, then returns the success it got.</summary>
        CommitThenWait,

        /// <summary>Waits, then commits under <c>Locked</c> once its generation is checked again.</summary>
        WaitThenCommit,

        /// <summary>A mutating tool whose body returns success without having committed anything.</summary>
        SuccessWithoutCommit,

        /// <summary>A read-only tool that returns the success of its <c>Locked</c> body after waiting.</summary>
        ReadThenWait,

        /// <summary>Waits, then faults with an ordinary exception.</summary>
        WaitThenFault,

        /// <summary>Commits under <c>Locked</c>, waits, then faults.</summary>
        CommitThenFault,
    }

    private sealed class GatedTool : ProbeTool
    {
        private readonly Body _body;

        public GatedTool(Body body) : base("test_gated")
        {
            _body = body;
        }

        public override bool IsMutating => _body != Body.ReadThenWait;

        public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ToolResult CommitApple(ToolContext context) => context.Locked(() =>
        {
            var e = context.World.CommitSpawn(new[] { new SpawnRequest("apple", SceneRegions.Center) }, SceneActors.Sensei, context.TurnId);
            return context.Success(e.InstanceIds);
        });

        public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
        {
            switch (_body)
            {
                case Body.CommitThenWait:
                {
                    var committed = CommitApple(context);
                    Reached.TrySetResult(true);
                    await Release.Task;
                    return committed;
                }
                case Body.WaitThenCommit:
                {
                    Reached.TrySetResult(true);
                    await Release.Task;
                    return CommitApple(context);
                }
                case Body.SuccessWithoutCommit:
                    Reached.TrySetResult(true);
                    await Release.Task;
                    return context.Success();
                case Body.ReadThenWait:
                {
                    var read = context.Read(() => context.Success());
                    Reached.TrySetResult(true);
                    await Release.Task;
                    return read;
                }
                case Body.CommitThenFault:
                {
                    CommitApple(context);
                    Reached.TrySetResult(true);
                    await Release.Task;
                    throw new IOException("dependency settled late");
                }
                default:
                    Reached.TrySetResult(true);
                    await Release.Task;
                    throw new IOException("dependency settled late");
            }
        }
    }

    public enum Transition
    {
        CancelGeneration,
        BeginNextTurn,
        Reset,
    }

    private static async Task Transit(TestEngine t, Transition transition)
    {
        switch (transition)
        {
            case Transition.CancelGeneration:
                t.Engine.CancelGeneration();
                break;
            case Transition.BeginNextTurn:
                t.Engine.BeginTurn("next");
                break;
            default:
                await t.Engine.ResetProfileAsync();
                break;
        }
    }

    private static async Task<(ToolResult Result, int Objects, long RevisionGain)> RunAsync(Body body, Transition transition)
    {
        var tool = new GatedTool(body);
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("first");
        var before = t.Engine.GetScene().Revision;
        var pending = t.CallAsync(tool.Name, """{"action":"ok"}""", "gated");
        await tool.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await Transit(t, transition); }
        finally { tool.Release.TrySetResult(true); }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        var after = t.Engine.GetScene();
        return (result, after.Instances.Count, after.Revision - before);
    }

    // ------------------------------------------------------------------ finding 3

    [Theory]
    [InlineData(Transition.CancelGeneration)]
    [InlineData(Transition.BeginNextTurn)]
    public async Task S22_a_commit_made_in_a_current_generation_keeps_its_success_when_the_generation_ends_afterwards(Transition transition)
    {
        var (result, objects, revisionGain) = await RunAsync(Body.CommitThenWait, transition);

        Assert.Equal(ToolStatuses.Success, result.Status);
        Assert.Equal(1, objects);
        Assert.Equal(1, revisionGain);
    }

    [Fact]
    public async Task S87_a_commit_made_before_a_reset_is_removed_and_its_call_is_cancelled()
    {
        var (result, objects, _) = await RunAsync(Body.CommitThenWait, Transition.Reset);

        Assert.Equal(ToolStatuses.Cancelled, result.Status);
        Assert.Equal(0, objects);
    }

    [Theory]
    [InlineData(Transition.CancelGeneration)]
    [InlineData(Transition.BeginNextTurn)]
    [InlineData(Transition.Reset)]
    public async Task S22_a_call_that_commits_nothing_before_its_generation_ends_is_cancelled_with_no_effect(Transition transition)
    {
        var (result, objects, revisionGain) = await RunAsync(Body.SuccessWithoutCommit, transition);

        Assert.Equal(ToolStatuses.Cancelled, result.Status);
        Assert.Equal(0, objects);
        Assert.True(revisionGain <= 0, "a reset of an empty scene never adds a revision for this call");
    }

    [Theory]
    [InlineData(Transition.CancelGeneration)]
    [InlineData(Transition.BeginNextTurn)]
    [InlineData(Transition.Reset)]
    public async Task S27_a_read_that_settles_after_its_generation_ended_is_cancelled(Transition transition)
    {
        var (result, objects, _) = await RunAsync(Body.ReadThenWait, transition);

        Assert.Equal(ToolStatuses.Cancelled, result.Status);
        Assert.Equal(0, objects);
    }

    [Theory]
    [InlineData(Transition.CancelGeneration)]
    [InlineData(Transition.BeginNextTurn)]
    [InlineData(Transition.Reset)]
    public async Task S22_the_result_says_success_exactly_when_the_scene_change_exists(Transition transition)
    {
        var (result, objects, _) = await RunAsync(Body.WaitThenCommit, transition);

        // The body re-checks its generation under the lock before it commits (the documented commit guard): nothing is
        // committed and the call is cancelled. Either way the report and the world agree.
        Assert.Equal(result.Status == ToolStatuses.Success, objects == 1);
        Assert.Equal(ToolStatuses.Cancelled, result.Status);
    }

    [Fact]
    public async Task S22_a_committed_success_is_what_a_duplicate_of_the_call_receives_too()
    {
        var tool = new GatedTool(Body.CommitThenWait);
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("first");
        var pending = t.CallAsync(tool.Name, """{"action":"ok"}""", "gated");
        await tool.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = t.CallAsync(tool.Name, """{"action":"ok"}""", "gated");
        try { t.Engine.CancelGeneration(); }
        finally { tool.Release.TrySetResult(true); }

        var first = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await duplicate.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ToolStatuses.Success, first.Status);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.AffectedIds, second.AffectedIds);
        Assert.Single(t.Engine.GetScene().Instances);
    }

    // ------------------------------------------------------------------ finding 4

    [Theory]
    [InlineData(Body.WaitThenFault, Transition.CancelGeneration, false)]
    [InlineData(Body.WaitThenFault, Transition.CancelGeneration, true)]
    [InlineData(Body.WaitThenFault, Transition.BeginNextTurn, false)]
    [InlineData(Body.WaitThenFault, Transition.BeginNextTurn, true)]
    [InlineData(Body.WaitThenFault, Transition.Reset, false)]
    [InlineData(Body.WaitThenFault, Transition.Reset, true)]
    [InlineData(Body.CommitThenFault, Transition.Reset, false)]
    [InlineData(Body.CommitThenFault, Transition.Reset, true)]
    public async Task S87_work_of_an_ended_generation_that_faults_late_is_cancelled_for_the_caller_and_for_a_duplicate(Body body, Transition transition, bool duplicateToo)
    {
        var tool = new GatedTool(body);
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("first");
        var pending = t.CallAsync(tool.Name, """{"action":"ok"}""", "late");
        await tool.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = duplicateToo ? t.CallAsync(tool.Name, """{"action":"ok"}""", "late") : null;
        try { await Transit(t, transition); }
        finally { tool.Release.TrySetResult(true); }

        var first = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ToolStatuses.Cancelled, first.Status);
        if (duplicate is not null)
        {
            var second = await duplicate.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ToolStatuses.Cancelled, second.Status);
        }

        // The late fault is not recorded as a tool error of the current engine state.
        Assert.DoesNotContain(t.Trace.Entries, e => e.CallId == "late" && e.Status == ToolStatuses.Failure);
    }

    [Fact]
    public async Task S87_a_fault_in_the_current_generation_is_still_an_error_for_the_caller_and_a_duplicate()
    {
        var tool = new GatedTool(Body.WaitThenFault);
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("first");
        var pending = t.CallAsync(tool.Name, """{"action":"ok"}""", "now");
        await tool.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = t.CallAsync(tool.Name, """{"action":"ok"}""", "now");
        tool.Release.TrySetResult(true);

        var firstError = await Record.ExceptionAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        var secondError = await Record.ExceptionAsync(() => duplicate.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.IsType<SenseiEngineException>(firstError);
        Assert.IsType<SenseiEngineException>(secondError);
        Assert.Contains(t.Trace.Entries, e => e.CallId == "now" && e.ErrorCode == ToolErrorCodes.InternalError);
    }
}

using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;

namespace Sensei.Engine.Tests.Foundation;

public sealed class CompletionAttribution
{
    private sealed class WaitingTool : ProbeTool
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WaitingTool() : base("test_wait") { }
        public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
        {
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(ct);
            return context.Locked(() => context.Success());
        }
    }

    [Fact]
    public async Task S35d_only_the_call_that_completes_the_task_reports_completion()
    {
        var waiter = new WaitingTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { waiter, new MutatingProbeTool() });
        Spawn(t, "apple", "left");
        t.Engine.BeginTurn("put an apple in the center");
        lock (t.Core.Sync)
        {
            var predicates = new[] { new TaskPredicate { Type = "quantity", Selector = new PredicateSelector { CatalogId = "apple" }, Region = "center", Count = 1 } };
            Assert.Null(t.Core.Tasks.ValidateStart(predicates, Array.Empty<ScenarioProperty>()));
            t.Core.Tasks.CommitStart("listen_arrange", new TaskInstruction { English = "Put an apple in the center" },
                new[] { new SkillTarget { Dimension = "listening", Key = "apple" } }, predicates, Array.Empty<ScenarioProperty>(), null, false, t.Core.CurrentTurnId);
        }
        t.Core.FlushNotifications();
        var unrelated = t.Engine.InvokeToolAsync("test_wait", "{\"action\":\"ok\"}");
        await waiter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ToolResult completing;
        try { completing = await t.Engine.InvokeToolAsync("test_mutate", "{\"action\":\"spawn\"}"); }
        finally { waiter.Release.TrySetResult(true); }
        var readResult = await unrelated.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("completed", completing.Data!.Value.GetProperty("task_status").GetString());
        Assert.False(readResult.Data is { } data && data.TryGetProperty("task_status", out _), "An unrelated overlapping call must not claim task completion.");
    }
}

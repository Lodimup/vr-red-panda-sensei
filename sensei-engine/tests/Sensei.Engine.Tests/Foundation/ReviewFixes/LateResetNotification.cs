using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.Foundation.RoundThree;

public sealed class LateResetNotification
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S87_late_cancelled_call_cannot_deliver_a_pre_reset_scene_snapshot(bool fault)
    {
        var tool = new CommitThenWait(fault);
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("first");
        var pending = t.CallAsync(tool.Name, """{"action":"ok"}""", "old");
        await tool.Committed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await t.Engine.ResetProfileAsync();
        Assert.Empty(t.Engine.GetScene().Instances);
        var notificationsAtReset = t.Events.SceneEvents.Count;
        Assert.Equal(SceneEventKinds.Removed, t.Events.SceneEvents.Last().Kind);
        tool.Release.TrySetResult(true);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ToolStatuses.Cancelled, result.Status);
        Assert.Empty(t.Engine.GetScene().Instances);
        var late = t.Events.SceneEvents.Skip(notificationsAtReset).ToList();
        Assert.True(late.Count == 0,
            $"S87: {late.Count} pre-reset scene notification(s) arrived after reset; last kind={t.Events.SceneEvents.Last().Kind}, " +
            $"last snapshot objects={t.Events.SceneSnapshots.Last().Instances.Count}, current objects={t.Engine.GetScene().Instances.Count}.");
    }

    private sealed class CommitThenWait(bool fault) : ProbeTool("test_late_scene")
    {
        public override bool IsMutating => true;
        public TaskCompletionSource<bool> Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
        {
            var result = context.Locked(() =>
            {
                var e = context.World.CommitSpawn(new[] { new SpawnRequest("apple", SceneRegions.Center) }, SceneActors.Sensei, context.TurnId);
                return context.Success(e.InstanceIds);
            });
            Committed.TrySetResult(true);
            await Release.Task;
            if (fault) throw new IOException("old work faulted");
            return result;
        }
    }
}

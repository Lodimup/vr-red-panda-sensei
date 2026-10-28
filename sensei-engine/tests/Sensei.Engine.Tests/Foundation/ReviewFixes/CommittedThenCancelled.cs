using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.Foundation.RoundTwo;

public sealed class CommittedThenCancelled
{
    private sealed class CommitTool : ProbeTool
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
            return result;
        }
    }

    [Fact]
    public async Task S22_cancellation_cannot_turn_a_committed_scene_change_into_a_no_effect_result()
    {
        var tool = new CommitTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("first");
        var before = t.Engine.GetScene();
        var pending = t.CallAsync(tool.Name, """{"action":"spawn"}""", "commit");
        await tool.Committed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { t.Engine.CancelGeneration(); }
        finally { tool.Release.TrySetResult(true); }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        var after = t.Engine.GetScene();

        Assert.True(result.Status == ToolStatuses.Success ||
                    (after.Revision == before.Revision && after.Instances.Count == before.Instances.Count),
            $"S22: result={result.Status}, revision={before.Revision}->{after.Revision}, objects={before.Instances.Count}->{after.Instances.Count}; non-speech effects survived a cancelled/failed call.");
    }
}

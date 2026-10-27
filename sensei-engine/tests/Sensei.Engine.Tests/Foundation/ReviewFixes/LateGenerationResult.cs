using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Tests.Foundation;

public sealed class LateGenerationResult
{
    private sealed class DelayedResultTool : ProbeTool
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
        {
            // A read completes before cancellation; its dependency/continuation settles later.
            var result = context.Locked(() => context.Success());
            Started.TrySetResult(true);
            await Release.Task;
            return result;
        }
    }

    [Fact]
    public async Task S87_a_pre_reset_call_that_settles_late_returns_cancelled()
    {
        var tool = new DelayedResultTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("before reset");
        var oldCall = t.Engine.InvokeToolAsync("test_probe", "{\"action\":\"ok\"}");
        await tool.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await t.Engine.ResetProfileAsync(); }
        finally { tool.Release.TrySetResult(true); }

        var result = await oldCall.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ToolStatuses.Cancelled, result.Status);
        Assert.Equal(ToolErrorCodes.Cancelled, result.ErrorCode);
    }
}

using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Tests.Foundation.RoundTwo;

public sealed class LateExceptionAfterReset
{
    private sealed class LateFailureTool : ProbeTool
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
        {
            Started.TrySetResult(true);
            await Release.Task;
            throw new IOException("dependency settled after reset");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S87_pre_reset_work_that_faults_late_returns_cancelled(bool checkDuplicate)
    {
        var tool = new LateFailureTool();
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("first");
        var pending = t.CallAsync(tool.Name, """{"action":"ok"}""", "late");
        await tool.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = t.CallAsync(tool.Name, """{"action":"ok"}""", "late");
        try { await t.Engine.ResetProfileAsync(); }
        finally { tool.Release.TrySetResult(true); }

        ToolResult? first = null;
        ToolResult? second = null;
        var error = await Record.ExceptionAsync(async () => first = await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        var duplicateError = await Record.ExceptionAsync(async () => second = await duplicate.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Null(checkDuplicate ? duplicateError : error);
        Assert.Equal(ToolStatuses.Cancelled, (checkDuplicate ? second : first)!.Status);
    }
}

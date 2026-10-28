using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Tests.Foundation.RoundTwo;

public sealed class CancellationExceptionSecret
{
    private const string Secret = "fictional-cancellation-credential";

    private sealed class CallbackTool : ProbeTool
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
        {
            using var registration = ct.Register(() => throw new IOException("speech cancel failed: " + Secret));
            Started.TrySetResult(true);
            await Release.Task;
            return context.Locked(() => context.Success());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S77_generation_transition_redacts_dependency_callback_exceptions(bool beginNextTurn)
    {
        var tool = new CallbackTool();
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret }, extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("first");
        var pending = t.CallAsync(tool.Name, """{"action":"ok"}""");
        await tool.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Exception? error;
        try
        {
            error = Record.Exception(() =>
            {
                if (beginNextTurn) t.Engine.BeginTurn("second");
                else t.Engine.CancelGeneration();
            });
        }
        finally { tool.Release.TrySetResult(true); }
        await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(error);
        Assert.DoesNotContain(Secret, error.ToString());
    }
}

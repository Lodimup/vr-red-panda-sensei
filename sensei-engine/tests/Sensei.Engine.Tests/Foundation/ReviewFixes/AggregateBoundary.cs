using System.Diagnostics;
using System.Reflection;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Foundation.RoundThree;

public sealed class AggregateBoundary
{
    // Use a child testhost: a stack overflow must become a normal failing assertion in the review's parent test run.
    [Theory]
    [InlineData("deep")]
    [InlineData("cycle")]
    public async Task S77_boundary_handles_aggregate_graph_without_crashing_the_host(string mode)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, "dotnet"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(AggregateBoundary).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName~AggregateBoundary.ChildProcessProbe");
        start.Environment["RPS_R3_AGGREGATE_MODE"] = mode;
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail($"{mode}: sanitization did not finish within 20 seconds");
        }
        var output = await stdout + await stderr;
        var excerpt = output.Length <= 1800 ? output : output[..900] + "\n...\n" + output[^900..];
        Assert.True(process.ExitCode == 0, $"{mode}: dependency exception crashed the child host (exit {process.ExitCode}).\n{excerpt}");
    }

    [Fact]
    public async Task ChildProcessProbe()
    {
        var mode = Environment.GetEnvironmentVariable("RPS_R3_AGGREGATE_MODE");
        if (mode is null) return;
        Exception error;
        if (mode == "deep")
        {
            error = new IOException("sk-abcdefghijklmnop123456");
            for (var i = 0; i < 30000; i++) error = new AggregateException("outer", error);
        }
        else
        {
            // Synthetic graph required to check cycle safety: AggregateException exposes no cycle-building constructor.
            var aggregate = new AggregateException("outer", new IOException("leaf"));
            var field = typeof(AggregateException).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(f => f.FieldType == typeof(Exception[]));
            ((Exception[])field.GetValue(aggregate)!)[0] = aggregate;
            error = aggregate;
        }
        var store = new InMemoryProfileStore();
        await using var t = await TestEngine.CreateAsync(store: store, trace: new RecordingTraceSink(),
            knownSecrets: new[] { "sk-abcdefghijklmnop123456" });
        store.FailSavesWith = error;
        var caught = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("zero"));
        Assert.IsType<SenseiEngineException>(caught);
        Assert.DoesNotContain("sk-abcdefghijklmnop123456", caught!.ToString());
    }
}

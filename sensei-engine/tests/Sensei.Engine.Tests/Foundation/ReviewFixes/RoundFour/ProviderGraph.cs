using System.Diagnostics;
using System.Reflection;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Foundation.ReviewFixes.RoundFour;

public sealed class ProviderGraph
{
    [Theory]
    [InlineData("deep")]
    [InlineData("cycle")]
    [InlineData("message")]
    public async Task F04_provider_exception_graph_must_not_crash_or_hang_host(string mode)
    {
        var start = new ProcessStartInfo((Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, "dotnet")))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(ProviderGraph).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName~RoundFour.ProviderGraph.ChildProcessProbe");
        start.Environment["RPS_R4_PROVIDER_GRAPH"] = mode;
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12)); }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail($"{mode}: provider exception handling hung for 12 seconds; child testhost killed");
        }
        var output = await stdout + await stderr;
        var excerpt = output.Length <= 1600 ? output : output[..1000] + "\n...\n" + output[^600..];
        Assert.True(process.ExitCode == 0, $"{mode}: provider exception crashed child host, exit {process.ExitCode}.\n{excerpt}");
    }

    [Fact]
    public async Task ChildProcessProbe()
    {
        var mode = Environment.GetEnvironmentVariable("RPS_R4_PROVIDER_GRAPH");
        if (mode is null) return;
        Exception error;
        if (mode == "deep")
        {
            error = new IOException("leaf");
            for (var i = 0; i < 30000; i++) error = new AggregateException("wrapper", error);
            // A multi-member root defeats Unwrap, then AggregateException.Message recursively formats the chain.
            error = new AggregateException("root", error, new IOException("sibling"));
        }
        else if (mode == "cycle")
        {
            var aggregate = new AggregateException("wrapper", new IOException("leaf"));
            var field = typeof(AggregateException).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(f => f.FieldType == typeof(Exception[]));
            ((Exception[])field.GetValue(aggregate)!)[0] = aggregate;
            error = aggregate;
        }
        else error = new RecursiveMessageException();
        var chat = new ScriptedChatClient().EnqueueError(error);
        await using var t = await TestEngine.CreateAsync(chat: chat);
        var result = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal(TurnErrorCodes.ProviderError, result.ErrorCode);
    }

    private sealed class RecursiveMessageException : Exception
    {
        public override string Message => Message;
    }
}

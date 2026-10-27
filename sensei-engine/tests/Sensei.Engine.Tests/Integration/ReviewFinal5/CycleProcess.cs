using System.Diagnostics;

namespace Sensei.Engine.Tests.Integration.ReviewFinal5;

internal static class CycleProcess
{
    // The probe project sits beside the test project and is built with the solution, in the same configuration.
    private static string ProbeAssembly()
    {
        var output = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var configuration = Path.GetFileName(Path.GetDirectoryName(output)!);
        var framework = Path.GetFileName(output);
        for (var dir = new DirectoryInfo(output); dir is not null; dir = dir.Parent)
        {
            var probe = Path.Combine(dir.FullName, "Sensei.Engine.CycleProbe", "bin", configuration, framework, "ReviewFinal5.CycleProbe.dll");
            if (File.Exists(probe)) return probe;
        }
        return Path.Combine(output, "ReviewFinal5.CycleProbe.dll");
    }

    internal static async Task<(int Code, string Output)> RunAsync(bool acyclic = false)
    {
        var project = ProbeAssembly();
        Assert.True(File.Exists(project), "Build the solution, which also builds Sensei.Engine.CycleProbe, first.");
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"), "dotnet");
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        start.ArgumentList.Add(project);
        if (acyclic) start.ArgumentList.Add("acyclic");
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            Assert.Fail("The cyclic-value probe hung instead of producing the specified not_serializable result.");
        }
        var output = await stdout + await stderr;
        return (child.ExitCode, output);
    }
}

public sealed class CycleRegressionTests
{
    [Fact]
    public async Task F02_cyclic_clr_backed_json_value_must_reject_without_ending_the_host()
    {
        var result = await CycleProcess.RunAsync();
        Assert.True(result.Code == 0,
            $"Expected a failed/not_serializable turn, one rejection trace and a live host; child exit={result.Code}; "
            + result.Output[..Math.Min(result.Output.Length, 900)]);
    }
}

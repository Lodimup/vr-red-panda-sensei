using System.Diagnostics;
using System.Text;
using Sensei.Engine.Tests.Agent;

namespace Sensei.Engine.Tests.Cli;

/// <summary>What one run of the <c>sensei</c> program produced.</summary>
public sealed record CliRun(int ExitCode, string Stdout, string Stderr, string ProfileDir)
{
    public IReadOnlyList<string> Lines => Stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The JSON lines of the session trace file, if one was written.</summary>
    public IReadOnlyList<string> TraceLines()
    {
        var dir = Path.Combine(ProfileDir, "traces");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir, "*.jsonl").SelectMany(File.ReadAllLines).ToList();
    }
}

/// <summary>
/// Runs the built CLI (<c>dotnet sensei.dll</c>) in a child process with a clean environment. The dll is found next to the
/// source tree; when it has not been built yet it is built once.
/// </summary>
public static class CliRunner
{
    private static readonly Lazy<string> SenseiDll = new(FindOrBuild);

    private static string RepoSensei()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var cli = Path.Combine(dir.FullName, "sensei-engine", "src", "Sensei.Engine.Cli");
            if (File.Exists(Path.Combine(cli, "Sensei.Engine.Cli.csproj"))) return cli;
        }
        throw new DirectoryNotFoundException("sensei-engine/src/Sensei.Engine.Cli not found above " + AppContext.BaseDirectory);
    }

    private static string? Find(string cliDir)
    {
        var bin = Path.Combine(cliDir, "bin");
        if (!Directory.Exists(bin)) return null;
        return Directory.GetFiles(bin, "sensei.dll", SearchOption.AllDirectories)
            .Where(p => p.Contains(Path.Combine("net10.0", "sensei.dll"), StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string FindOrBuild()
    {
        var cliDir = RepoSensei();
        var dll = Find(cliDir);
        if (dll is not null) return dll;
        var build = Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            ArgumentList = { "build", Path.Combine(cliDir, "Sensei.Engine.Cli.csproj"), "--nologo", "-v", "q" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        build.StandardOutput.ReadToEnd();
        build.WaitForExit();
        return Find(cliDir) ?? throw new FileNotFoundException("sensei.dll was not built");
    }

    public static string NewTempDirectory() => Directory.CreateTempSubdirectory("sensei-cli-test-").FullName;

    /// <summary>Runs the CLI with <paramref name="lines"/> as a script file (or as stdin when <paramref name="viaStdin"/>).</summary>
    public static async Task<CliRun> RunScriptAsync(string[] lines, IDictionary<string, string?>? env = null, string[]? extraArgs = null,
        string? profileDir = null, string? pathPrefix = null)
    {
        profileDir ??= NewTempDirectory();
        var script = Path.Combine(NewTempDirectory(), "script.txt");
        await File.WriteAllLinesAsync(script, lines, new UTF8Encoding(false));
        var args = new List<string> { "--profile-dir", profileDir, "--script", script };
        if (extraArgs is not null) args.AddRange(extraArgs);
        return await RunAsync(args, null, env, profileDir, pathPrefix);
    }

    public static async Task<CliRun> RunAsync(IEnumerable<string> args, string? stdin = null, IDictionary<string, string?>? env = null,
        string? profileDir = null, string? pathPrefix = null, TimeSpan? timeout = null)
    {
        profileDir ??= NewTempDirectory();
        var info = new ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = profileDir,
        };
        info.ArgumentList.Add(SenseiDll.Value);
        foreach (var a in args) info.ArgumentList.Add(a);
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("SENSEI_LLM_", StringComparison.Ordinal)).ToList()) info.Environment.Remove(key);
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        if (pathPrefix is not null) info.Environment["PATH"] = pathPrefix + Path.PathSeparator + info.Environment["PATH"];
        if (env is not null)
            foreach (var (key, value) in env)
            {
                if (value is null) info.Environment.Remove(key);
                else info.Environment[key] = value;
            }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (stdin is not null) await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("the CLI did not finish in time; output so far:\n" + await stdout);
        }
        return new CliRun(process.ExitCode, await stdout, await stderr, profileDir);
    }

    /// <summary>Environment that points the CLI at a loopback fake endpoint.</summary>
    public static Dictionary<string, string?> EnvFor(FakeChatServer server, string key = "cli-test-key-0123456789") => new()
    {
        ["SENSEI_LLM_PROVIDER"] = "openai_compatible",
        ["SENSEI_LLM_API_KEY"] = key,
        ["SENSEI_LLM_BASE_URL"] = server.BaseUrl,
        ["SENSEI_LLM_MODEL"] = "cli-test-model",
    };
}

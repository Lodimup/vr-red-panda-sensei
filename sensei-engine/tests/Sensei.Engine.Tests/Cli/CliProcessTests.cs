using System.Text.RegularExpressions;
using Sensei.Engine.Tests.Agent;

namespace Sensei.Engine.Tests.Cli;

/// <summary>
/// The real built program (<c>dotnet sensei.dll</c> in a child process): exit codes, stdin, UTF-8 over pipes, the profile on
/// disk and <c>--voice say</c> through <c>PATH</c>. A model, where one is needed, is a loopback fake endpoint.
/// </summary>
public sealed class CliProcessTests
{
    private const string Key = "process-key-0123456789abcdef";
    private const string ResultPattern = @"^Sensei: [a-z ]+ \(turn \d+: \w+, \d+ rounds, \d+ tools, \d+ ms\)$";

    // ---------------------------------------------------------------- S74 / S110 exit codes of the real program

    [Fact]
    public async Task S110_the_real_program_exits_0_when_nothing_failed()
    {
        var run = await CliRunner.RunScriptAsync(new[] { "/scene", "/tool no_such_tool {}" });
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Scene rev 0, selected: none", run.Lines);
    }

    [Fact]
    public async Task S74_the_real_program_without_a_key_prints_the_error_line_and_exits_2()
    {
        var run = await CliRunner.RunScriptAsync(new[] { "hello", "/scene" });

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("error: SENSEI_LLM_API_KEY is not set", run.Lines);
        Assert.Contains("Scene rev 0, selected: none", run.Lines);
        Assert.Matches(ResultPattern, Assert.Single(run.Lines, l => l.StartsWith("Sensei: ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task S110_the_real_program_exits_1_for_an_unknown_command_even_after_an_llm_error()
    {
        var run = await CliRunner.RunScriptAsync(new[] { "hello", "/bogus" });
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: unknown command '/bogus' (try /help)", run.Lines);
    }

    [Fact]
    public async Task S110_the_real_program_exits_1_for_invalid_command_arguments()
    {
        var run = await CliRunner.RunScriptAsync(new[] { "/band nonsense" });
        Assert.Equal(1, run.ExitCode);
    }

    [Fact]
    public async Task S13_the_real_program_handles_help_and_bad_options()
    {
        var help = await CliRunner.RunAsync(new[] { "--help" });
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("usage: sensei", help.Stdout);

        var bad = await CliRunner.RunAsync(new[] { "--bogus" });
        Assert.Equal(1, bad.ExitCode);
        Assert.Contains("error: unknown option '--bogus'", bad.Stdout);
    }

    // ---------------------------------------------------------------- S59 / S73 files

    [Fact]
    public async Task S59_the_profile_survives_between_two_runs_of_the_real_program()
    {
        var dir = CliRunner.NewTempDirectory();
        await CliRunner.RunScriptAsync(new[] { "/band advanced" }, profileDir: dir);

        var second = await CliRunner.RunScriptAsync(new[] { "/profile" }, profileDir: dir);

        Assert.Contains("starting band: advanced", second.Lines);
        Assert.Contains("working level: n2 (hidden from learner)", second.Lines);
        Assert.True(File.Exists(Path.Combine(dir, "default.json")));
    }

    [Fact]
    public async Task S73_the_real_program_writes_a_trace_file_for_the_session()
    {
        var run = await CliRunner.RunScriptAsync(new[] { "/tool no_such_tool {}" });
        var line = Assert.Single(run.TraceLines());
        Assert.Contains("\"tool\":\"no_such_tool\"", line);
        Assert.Contains("\"status\":\"failure\"", line);
    }

    // ---------------------------------------------------------------- S70 a turn over HTTP, UTF-8 over pipes

    [Fact]
    public async Task S70_the_real_program_runs_a_turn_against_a_model_and_prints_japanese_speech()
    {
        using var server = new FakeChatServer()
            .EnqueueToolCalls(("call_1", "speak", """{"segments":[{"lang":"ja","text":"水をください","reading":"みずをください"},{"lang":"en","text":"Water, please."}]}"""))
            .EnqueueText("finished");

        var run = await CliRunner.RunScriptAsync(new[] { "お水ください" }, CliRunner.EnvFor(server, Key));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Sensei [ja]: 水をください | reading: みずをください", run.Lines);
        Assert.Contains("Sensei [en]: Water, please.", run.Lines);
        Assert.Matches(ResultPattern, Assert.Single(run.Lines, l => l.StartsWith("Sensei: ", StringComparison.Ordinal)));
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal("Bearer " + Key, server.Requests[0].Authorization);
        Assert.Equal("cli-test-model", server.Requests[0].Json.GetProperty("model").GetString());
        var messages = server.Requests[0].Json.GetProperty("messages").EnumerateArray().ToList();
        Assert.Contains("お水ください", messages[^1].GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task S77_the_real_program_never_prints_stores_or_sends_the_key_in_a_message()
    {
        using var server = new FakeChatServer().EnqueueText("ok");

        var run = await CliRunner.RunScriptAsync(new[] { "my key is " + Key, "/tool " + Key + " {}" }, CliRunner.EnvFor(server, Key));

        Assert.DoesNotContain(Key, run.Stdout);
        Assert.DoesNotContain(Key, run.Stderr);
        foreach (var file in Directory.GetFiles(run.ProfileDir, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(Key, await File.ReadAllTextAsync(file));
        Assert.DoesNotContain(Key, server.Requests[0].Body);
    }

    [Theory]
    [InlineData(401, "auth_failed")]
    [InlineData(403, "auth_failed")]
    [InlineData(402, "quota_exceeded")]
    public async Task S67_the_real_program_reports_provider_errors_and_exits_2(int status, string code)
    {
        using var server = new FakeChatServer().EnqueueStatus(status);

        var run = await CliRunner.RunScriptAsync(new[] { "hello" }, CliRunner.EnvFor(server, Key));

        Assert.Equal(2, run.ExitCode);
        Assert.Contains(run.Lines, l => l.StartsWith($"error: {code}:", StringComparison.Ordinal));
        Assert.DoesNotContain(Key, run.Stdout);
    }

    // ---------------------------------------------------------------- S109 interactive over stdin

    [Fact]
    public async Task S109_the_real_program_reads_utterances_from_stdin_and_waits_for_the_turn_at_end_of_input()
    {
        using var server = new FakeChatServer().EnqueueText("slow answer", delay: TimeSpan.FromMilliseconds(400));

        var run = await CliRunner.RunAsync(new string[0], "hello\n", CliRunner.EnvFor(server, Key));

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("Red Panda Sensei - session s-", run.Lines[0]);
        Assert.Matches(ResultPattern, Assert.Single(run.Lines, l => l.StartsWith("Sensei: ", StringComparison.Ordinal)));
        Assert.Contains("turn 1: completed", run.Stdout);
    }

    [Fact]
    public async Task S109_the_real_program_answers_commands_while_a_turn_is_running_and_quits_on_quit()
    {
        using var server = new FakeChatServer().EnqueueText("never delivered", delay: TimeSpan.FromSeconds(30));

        var run = await CliRunner.RunAsync(new string[0], "hello\n/scene\n/quit\n", CliRunner.EnvFor(server, Key));

        var scene = run.Lines.ToList().FindIndex(l => l.StartsWith("Scene rev", StringComparison.Ordinal));
        var interrupted = run.Lines.ToList().FindIndex(l => l.StartsWith("Sensei: interrupted", StringComparison.Ordinal));
        Assert.True(scene >= 0 && interrupted > scene, run.Stdout);
        Assert.Equal(0, run.ExitCode);
    }

    // ---------------------------------------------------------------- S118 --voice say through PATH

    [Fact]
    public async Task S118_the_real_program_with_voice_say_runs_the_program_named_say_found_through_path()
    {
        var bin = CliRunner.NewTempDirectory();
        var log = Path.Combine(bin, "say.log");
        var script = Path.Combine(bin, "say");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf '%s\\n' \"$@\" >> \"$SENSEI_FAKE_SAY_LOG\"\nprintf -- '--call--\\n' >> \"$SENSEI_FAKE_SAY_LOG\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var server = new FakeChatServer()
            .EnqueueToolCalls(("call_1", "speak", """{"segments":[{"lang":"ja","text":"こんにちは"},{"lang":"en","text":"Hello there."}],"pace":"slow"}"""))
            .EnqueueText("done");
        var env = CliRunner.EnvFor(server, Key);
        env["SENSEI_FAKE_SAY_LOG"] = log;

        var run = await CliRunner.RunScriptAsync(new[] { "hi" }, env, new[] { "--voice", "say" }, pathPrefix: bin);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Sensei [ja]: こんにちは", run.Lines);
        var calls = (await File.ReadAllTextAsync(log)).Split("--call--\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, calls.Length);
        Assert.Equal("-v\nKyoko\n-r\n120\nこんにちは\n", calls[0]);
        Assert.Equal("-v\nSamantha\n-r\n120\nHello there.\n", calls[1]);
    }

    [Fact]
    public async Task S118_voice_console_never_runs_say()
    {
        var bin = CliRunner.NewTempDirectory();
        var log = Path.Combine(bin, "say.log");
        var script = Path.Combine(bin, "say");
        await File.WriteAllTextAsync(script, "#!/bin/sh\necho called >> \"$SENSEI_FAKE_SAY_LOG\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var server = new FakeChatServer()
            .EnqueueToolCalls(("call_1", "speak", """{"segments":[{"lang":"en","text":"Hello there."}]}"""))
            .EnqueueText("done");
        var env = CliRunner.EnvFor(server, Key);
        env["SENSEI_FAKE_SAY_LOG"] = log;

        var run = await CliRunner.RunScriptAsync(new[] { "hi" }, env, pathPrefix: bin);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Sensei [en]: Hello there.", run.Lines);
        Assert.False(File.Exists(log));
    }
}

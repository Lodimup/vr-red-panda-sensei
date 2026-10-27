using System.Text.Json;
using Sensei.Engine.Tests.Cli;
using static Sensei.Engine.Tests.Cli.InProcessCli;

namespace Sensei.Engine.Tests.Integration;

/// <summary>CLI output and exit codes the way SPEC §13 and S110 state them (S71, S72, S110).</summary>
public sealed class CliAcceptanceTests
{
    private const string SpawnCup = "/tool spawn_objects {\"objects\":[{\"catalog_id\":\"cup\"}]}";

    private static IEnumerable<JsonElement> DataLines(CliResult run) =>
        run.Lines.Where(l => l.StartsWith('{')).Select(l => JsonDocument.Parse(l).RootElement.Clone());

    // ---------------------------------------------------------------- S71 / S72: the data of /tool is one line of JSON

    [Fact]
    public async Task S71_select_marks_the_object()
    {
        var run = await ScriptAsync(new[] { SpawnCup, "/select cup_1", "/tool inspect_scene {}" });

        Assert.Equal(0, run.Code);
        var scene = Assert.Single(DataLines(run));
        Assert.Equal("cup_1", scene.GetProperty("selected_id").GetString());
    }

    [Fact]
    public async Task S72_tool_prints_the_status_line_and_one_line_of_data()
    {
        var run = await ScriptAsync(new[] { SpawnCup, "/tool inspect_scene {}" });

        var lines = run.Lines.ToList();
        var status = lines.IndexOf("[tool] inspect_scene success rev=1");
        Assert.True(status >= 0, run.Text);
        Assert.StartsWith("{", lines[status + 1]);
        var data = JsonDocument.Parse(lines[status + 1]).RootElement;
        Assert.Equal("cup_1", data.GetProperty("instances")[0].GetProperty("id").GetString());
        Assert.DoesNotContain(lines, l => l.StartsWith("data:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S72_a_tool_without_data_prints_only_the_status_line()
    {
        var run = await ScriptAsync(new[] { SpawnCup });
        Assert.Equal(new[] { "[tool] spawn_objects success rev=1" }, run.Lines);
    }

    // ---------------------------------------------------------------- S110 exit codes

    [Fact]
    public async Task S110_nothing_to_correct_is_not_an_arbitrary_exit_code()
    {
        var run = await ScriptAsync(new[] { "/correct hello" });
        Assert.Equal(0, run.Code);
    }

    [Fact]
    public async Task S110_correct_with_nothing_to_correct_is_reported_and_is_not_a_crash()
    {
        var run = await ScriptAsync(new[] { "/correct hello" });
        Assert.Contains(run.Lines, l => l.StartsWith("error: nothing_to_correct:", StringComparison.Ordinal));
        Assert.Equal(0, run.Code);
    }

    [Theory]
    [InlineData("/tool spawn_objects")]
    [InlineData("/tool inspect_scene")]
    public async Task S110_invalid_command_arguments_exit_1_tool_without_json(string line)
    {
        var run = await ScriptAsync(new[] { line });
        Assert.Equal(1, run.Code);
        Assert.DoesNotContain(run.Lines, l => l.StartsWith("[tool]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S110_a_tool_failure_and_text_that_is_not_json_are_still_normal_outcomes()
    {
        var run = await ScriptAsync(new[] { "/tool spawn_objects {}", "/tool inspect_scene not-json" });
        Assert.Equal(0, run.Code);
    }

    [Fact]
    public async Task S110_filesystem_exception_exits_one()
    {
        var file = Path.Combine(CliRunner.NewTempDirectory(), "occupied");
        await File.WriteAllTextAsync(file, "x");

        var run = await ScriptAsync(new[] { "/scene" }, profileDir: file);

        Assert.Equal(1, run.Code);
        Assert.Contains(run.Lines, l => l.StartsWith("error: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S110_the_real_program_exits_one_for_a_filesystem_exception()
    {
        var file = Path.Combine(CliRunner.NewTempDirectory(), "occupied");
        await File.WriteAllTextAsync(file, "x");
        var script = Path.Combine(CliRunner.NewTempDirectory(), "script.txt");
        await File.WriteAllTextAsync(script, "/scene\n");

        var run = await CliRunner.RunAsync(new[] { "--profile-dir", file, "--script", script }, profileDir: CliRunner.NewTempDirectory());

        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain("Unhandled exception", run.Stderr);
    }
}

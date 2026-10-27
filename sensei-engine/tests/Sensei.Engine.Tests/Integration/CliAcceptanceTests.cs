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

/// <summary>S109: interactive mode keeps answering while a turn waits on the provider.</summary>
public sealed class CliInteractiveAcceptanceTests
{
    private const string SpawnCup = "/tool spawn_objects {\"objects\":[{\"catalog_id\":\"cup\"}]}";

    private static FuncChatClient BlockedProvider() => new(async (_, _, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        return new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "never"));
    });

    [Theory]
    [InlineData("/select cup_1")]
    [InlineData("/move cup_1 left")]
    public async Task S109_interactive_select_and_move_act_before_the_old_response(string command)
    {
        var lines = new[] { SpawnCup, "hello", command, "/tool inspect_scene {}", "/quit" };
        var run = await InteractiveAsync(new StringReader(string.Join("\n", lines) + "\n"), BlockedProvider());

        var all = run.Lines.ToList();
        var interrupted = all.FindIndex(l => l.StartsWith("Sensei: interrupted", StringComparison.Ordinal));
        var dataIndex = all.FindIndex(l => l.StartsWith('{'));
        Assert.True(dataIndex >= 0, run.Text);
        Assert.True(interrupted > dataIndex, run.Text);
        var scene = JsonDocument.Parse(all[dataIndex]).RootElement;
        if (command.StartsWith("/select", StringComparison.Ordinal)) Assert.Equal("cup_1", scene.GetProperty("selected_id").GetString());
        else Assert.Equal("left", scene.GetProperty("instances")[0].GetProperty("region").GetString());
        Assert.DoesNotContain(all, l => l.Contains("turn_in_progress"));
    }

    [Fact]
    public async Task S109_a_tool_call_during_a_running_turn_does_not_cancel_it()
    {
        var lines = new[] { SpawnCup, "hello", "/tool inspect_scene {}" };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FuncChatClient(async (_, _, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "ok"));
        });
        var reader = new GatedReader(lines, () => gate.TrySetResult());

        var run = await InteractiveAsync(reader, provider);

        Assert.Contains(run.ResultLines, l => l.Contains("turn 2: completed"));
    }

    /// <summary>Hands out its lines one by one; after the last it runs <paramref name="atEnd"/> and reports end of input.</summary>
    private sealed class GatedReader : TextReader
    {
        private readonly Queue<string> _lines;
        private readonly Action _atEnd;

        public GatedReader(IEnumerable<string> lines, Action atEnd)
        {
            _lines = new Queue<string>(lines);
            _atEnd = atEnd;
        }

        public override string? ReadLine()
        {
            if (_lines.Count > 0) return _lines.Dequeue();
            _atEnd();
            return null;
        }

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => new(ReadLine());
    }
}

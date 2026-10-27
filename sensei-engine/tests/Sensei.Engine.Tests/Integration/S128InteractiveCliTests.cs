using System.Text;
using Microsoft.Extensions.AI;
using Sensei.Engine.Cli;
using Sensei.Engine.Tests.Cli;
using static Sensei.Engine.Tests.Cli.InProcessCli;

namespace Sensei.Engine.Tests.Integration;

/// <summary>
/// S128 (S71, S87, S109): in interactive mode <c>/select</c>, <c>/move</c>, <c>/scene</c> and <c>/profile</c> act at once while a turn runs;
/// <c>/tool</c> while a turn runs prints <c>turn_in_progress</c> and changes nothing; with no turn running it begins the manual turn.
/// And the <c>/scene</c> output format.
/// </summary>
public sealed class S128InteractiveCliTests
{
    private const string SpawnCup = "/tool spawn_objects {\"objects\":[{\"catalog_id\":\"cup\"}]}";
    private const string SceneHeader = "id | catalog | region | slot | in | japanese | reading | english";

    private static FuncChatClient BlockedProvider(TaskCompletionSource? release = null) => new(async (_, _, ct) =>
    {
        if (release is null) await Task.Delay(Timeout.Infinite, ct);
        else await release.Task.WaitAsync(ct);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
    });

    // ---------------------------------------------------------------- observation with /scene while the provider is blocked

    [Theory]
    [InlineData("/select cup_1")]
    [InlineData("/move cup_1 left")]
    public async Task S109_interactive_select_and_move_act_before_the_old_response(string command)
    {
        var lines = new[] { SpawnCup, "hello", command, "/scene", "/quit" };
        var run = await InteractiveAsync(new StringReader(string.Join("\n", lines) + "\n"), BlockedProvider());

        var all = run.Lines.ToList();
        var interrupted = all.FindIndex(l => l.StartsWith("Sensei: interrupted", StringComparison.Ordinal));
        var scene = all.FindIndex(l => l.StartsWith("Scene rev", StringComparison.Ordinal));
        Assert.True(scene >= 0, run.Text);
        Assert.True(interrupted > scene, run.Text);
        if (command.StartsWith("/select", StringComparison.Ordinal)) Assert.Equal("Scene rev 1, selected: cup_1", all[scene]);
        else
        {
            Assert.Equal("Scene rev 2, selected: none", all[scene]);
            Assert.StartsWith("cup_1 | cup | left | 1 | - | ", all[scene + 2]);
        }
    }

    [Fact]
    public async Task S128_scene_and_profile_act_immediately_while_a_turn_runs()
    {
        var run = await InteractiveAsync(new StringReader("hello\n/scene\n/profile\n/quit\n"), BlockedProvider());

        var all = run.Lines.ToList();
        var interrupted = all.FindIndex(l => l.StartsWith("Sensei: interrupted", StringComparison.Ordinal));
        var scene = all.FindIndex(l => l.StartsWith("Scene rev", StringComparison.Ordinal));
        var profile = all.FindIndex(l => l.StartsWith("profile default (session ", StringComparison.Ordinal));
        Assert.True(scene >= 0 && profile > scene && interrupted > profile, run.Text);
        Assert.Contains(all, l => l.StartsWith("working level: ", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- /tool while a turn runs, and when none does

    [Fact]
    public async Task S128_tool_while_a_turn_runs_prints_turn_in_progress_and_changes_nothing()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lines = new[] { "hello", SpawnCup, "/tool inspect_scene {}", "/scene" };
        var reader = new GatedReader(lines, () => release.TrySetResult());

        var run = await InteractiveAsync(reader, BlockedProvider(release));

        var all = run.Lines.ToList();
        Assert.Equal(2, all.Count(l => l.StartsWith("error: turn_in_progress", StringComparison.Ordinal)));
        Assert.DoesNotContain(all, l => l.StartsWith("[tool]", StringComparison.Ordinal));
        Assert.DoesNotContain(all, l => l.StartsWith('{'));
        Assert.Contains("Scene rev 0, selected: none", all);
        // The tool did not touch the running turn: it finished normally, and the session still exits 0 (a normal outcome, S110).
        Assert.Contains(run.ResultLines, l => l.Contains("turn 1: completed"));
        Assert.Equal(0, run.Code);
    }

    [Fact]
    public async Task S128_with_no_turn_running_tool_begins_the_manual_turn()
    {
        var output = new LockedWriter();
        var provider = new FuncChatClient((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        var reader = new StepReader(output,
            ("/tool inspect_scene {}", null),
            ("hello", null),
            ("/tool inspect_scene {}", "turn 2: completed"),
            ("/quit", "[tool] inspect_scene success rev=0"));

        var code = await CliApp.RunAsync(new[] { "--profile-dir", CliRunner.NewTempDirectory() }, reader, output, NoEnv, default, provider);

        var all = output.Lines;
        // At startup, and again once the utterance's turn has finished, /tool is accepted (each begins a manual turn).
        Assert.Equal(2, all.Count(l => l == "[tool] inspect_scene success rev=0"));
        Assert.DoesNotContain(all, l => l.Contains("turn_in_progress"));
        Assert.Equal(0, code);
    }

    // ---------------------------------------------------------------- /scene output (S128)

    [Fact]
    public async Task S128_scene_prints_the_revision_line_the_exact_header_and_one_line_per_instance()
    {
        var run = await ScriptAsync(new[]
        {
            "/tool spawn_objects {\"objects\":[{\"catalog_id\":\"box\"},{\"catalog_id\":\"cup\",\"region\":\"right\"}]}",
            "/select cup_1",
            "/scene",
        });

        var all = run.Lines.ToList();
        var first = all.FindIndex(l => l.StartsWith("Scene rev", StringComparison.Ordinal));
        Assert.Equal("Scene rev 1, selected: cup_1", all[first]);
        Assert.Equal(SceneHeader, all[first + 1]);
        Assert.Equal("box_1 | box | center | 1 | - | 箱 | はこ | box", all[first + 2]);
        Assert.Equal("cup_1 | cup | right | 1 | - | コップ | コップ | cup", all[first + 3]);
        Assert.Equal(first + 4, all.Count);
    }

    [Fact]
    public async Task S128_scene_names_the_container_in_the_in_column()
    {
        var run = await ScriptAsync(new[]
        {
            "/tool spawn_objects {\"objects\":[{\"catalog_id\":\"bag\"},{\"catalog_id\":\"pen\"}]}",
            "/move pen_1 bag_1",
            "/scene",
        });
        Assert.Contains("pen_1 | pen | center | 2 | bag_1 | ペン | ペン | pen", run.Lines);
        Assert.Contains("bag_1 | bag | center | 1 | - | かばん | かばん | bag", run.Lines);
    }

    [Fact]
    public async Task S128_scene_ends_with_the_active_task_line()
    {
        var start = "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"Ask for the cup.\"},\"skill_targets\":[{\"dimension\":\"vocabulary\",\"key\":\"cup\"}],"
                    + "\"predicates\":[{\"type\":\"in_region\",\"object_id\":\"cup_1\",\"region\":\"left\"}]}";
        var run = await ScriptAsync(new[]
        {
            SpawnCup,
            "ask",                       // a new turn, so the cup is visible (S84)
            "/tool start_challenge " + start,
            "/scene",
        });

        var all = run.Lines.ToList();
        var scene = all.FindIndex(l => l.StartsWith("Scene rev", StringComparison.Ordinal));
        Assert.Equal("Task t1 [discover_request] active: Ask for the cup.", all[^1]);
        Assert.Equal(SceneHeader, all[scene + 1]);
        Assert.Equal(scene + 4, all.Count);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A writer that can be read while the CLI is still writing to it.</summary>
    private sealed class LockedWriter : TextWriter
    {
        private readonly StringBuilder _text = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_text) _text.Append(value);
        }

        public override void Write(string? value)
        {
            lock (_text) _text.Append(value);
        }

        public string Text
        {
            get { lock (_text) return _text.ToString(); }
        }

        public IReadOnlyList<string> Lines => Text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Hands out its lines one by one; each may first wait until the output holds some text. After the last it reports end of input.</summary>
    private sealed class StepReader : TextReader
    {
        private readonly LockedWriter _output;
        private readonly Queue<(string Line, string? WaitFor)> _steps;

        public StepReader(LockedWriter output, params (string Line, string? WaitFor)[] steps)
        {
            _output = output;
            _steps = new Queue<(string, string?)>(steps);
        }

        public override string? ReadLine()
        {
            if (_steps.Count == 0) return null;
            var (line, waitFor) = _steps.Dequeue();
            if (waitFor is not null)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!_output.Text.Contains(waitFor, StringComparison.Ordinal))
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("the CLI never printed '" + waitFor + "':\n" + _output.Text);
                    Thread.Sleep(5);
                }
            }
            return line;
        }

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => new(ReadLine());
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

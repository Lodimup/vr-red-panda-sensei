using System.ClientModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;
using static Sensei.Engine.Tests.Cli.InProcessCli;

namespace Sensei.Engine.Tests.Cli;

public sealed class CliAppTests
{
    private const string ResultPattern = @"^Sensei: [a-z ]+ \(turn (\d+): (\w+), (\d+) rounds, (\d+) tools, (\d+) ms\)$";

    private const string SceneColumnHeader = "id | catalog | region | slot | in | japanese | reading | english";

    private const string SpeakJson = """{"segments":[{"lang":"ja","text":"水をください"},{"lang":"en","text":"Water, please."}]}""";

    private static string Spawn(params string[] catalogIds) =>
        "/tool spawn_objects {\"objects\":[" + string.Join(",", catalogIds.Select(c => $"{{\"catalog_id\":\"{c}\"}}")) + "]}";

    // ---------------------------------------------------------------- S70 / S75 turns

    [Fact]
    public async Task S70_a_learner_line_runs_a_turn_and_prints_the_result_line()
    {
        var chat = new ScriptedChatClient().EnqueueText("hello");
        var run = await ScriptAsync(new[] { "こんにちは" }, chat);

        Assert.Equal(0, run.Code);
        var line = Assert.Single(run.ResultLines);
        var m = Regex.Match(line, ResultPattern);
        Assert.True(m.Success, line);
        Assert.Equal("1", m.Groups[1].Value);
        Assert.Equal("completed", m.Groups[2].Value);
        Assert.Equal("1", m.Groups[3].Value);
        Assert.Equal("0", m.Groups[4].Value);
        Assert.Contains("こんにちは", AgentTestSupport.LearnerBlock(chat.Requests[0]));
    }

    [Fact]
    public async Task S75_each_turn_gets_its_own_result_line_with_its_turn_id()
    {
        var chat = new ScriptedChatClient().EnqueueText("a").EnqueueText("b");
        var run = await ScriptAsync(new[] { "one", "two" }, chat);

        var ids = run.ResultLines.Select(l => Regex.Match(l, ResultPattern).Groups[1].Value).ToArray();
        Assert.Equal(new[] { "1", "2" }, ids);
    }

    [Fact]
    public async Task S75_the_result_line_counts_rounds_and_tool_calls()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "speak", SpeakJson)).EnqueueText("done");
        var run = await ScriptAsync(new[] { "water" }, chat);

        var m = Regex.Match(Assert.Single(run.ResultLines), ResultPattern);
        Assert.Equal("2", m.Groups[3].Value);
        Assert.Equal("1", m.Groups[4].Value);
    }

    [Fact]
    public async Task S70_sensei_speech_prints_one_line_per_segment_before_the_result_line()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "speak", SpeakJson)).EnqueueText("done");
        var run = await ScriptAsync(new[] { "water" }, chat);

        var lines = run.Lines;
        var ja = run.IndexOf(l => l == "Sensei [ja]: 水をください");
        var en = run.IndexOf(l => l == "Sensei [en]: Water, please.");
        Assert.True(ja >= 0 && en == ja + 1, string.Join("\n", lines));
        Assert.True(run.IndexOf(l => l.StartsWith("Sensei: ", StringComparison.Ordinal)) > en);
    }

    [Fact]
    public async Task S91_hidden_japanese_prints_as_audio_in_the_cli()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "speak", SpeakJson)).EnqueueText("done");
        var run = await ScriptAsync(new[] { "/pref show_japanese false", "water" }, chat);

        Assert.Contains("Sensei [ja]: (audio)", run.Lines);
        Assert.DoesNotContain(run.Lines, l => l.Contains("水をください"));
        Assert.Contains("Sensei [en]: Water, please.", run.Lines);
    }

    [Fact]
    public async Task S70_hint_cards_print_as_card_lines()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "show_hint", """{"kind":"vocabulary_card","content":{"japanese":"水","reading":"みず","english":"water"}}"""))
            .EnqueueText("done");
        var run = await ScriptAsync(new[] { "what is water" }, chat);

        Assert.Contains("Card [vocabulary_card]: 水 / みず / water", run.Lines);
    }

    [Fact]
    public async Task S72_trace_prints_one_tool_line_per_call_and_the_unspoken_text()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "speak", SpeakJson)).EnqueueText("a private note");
        var run = await ScriptAsync(new[] { "water" }, chat, extra: new[] { "--trace" });

        Assert.Single(run.Lines, l => l == "[tool] speak success rev=0");
        Assert.Contains("[unspoken] a private note", run.Lines);
    }

    [Fact]
    public async Task S72_without_trace_agent_tool_calls_and_plain_text_print_nothing()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "speak", SpeakJson)).EnqueueText("a private note");
        var run = await ScriptAsync(new[] { "water" }, chat);

        Assert.DoesNotContain(run.Lines, l => l.StartsWith("[tool]", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Lines, l => l.Contains("a private note"));
    }

    [Fact]
    public async Task S73_every_call_is_appended_to_the_session_trace_file_with_its_result()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "speak", SpeakJson), ("c2", "no_such_tool", "{}")).EnqueueText("done");
        var run = await ScriptAsync(new[] { "water" }, chat);

        var files = Directory.GetFiles(Path.Combine(run.ProfileDir, "traces"), "*.jsonl");
        var file = Assert.Single(files);
        var entries = File.ReadAllLines(file).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { "speak", "no_such_tool" }, entries.Select(e => e.GetProperty("tool").GetString()).ToArray());
        Assert.Equal("success", entries[0].GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("unknown_tool", entries[1].GetProperty("error_code").GetString());
        Assert.Equal(Path.GetFileNameWithoutExtension(file), entries[0].GetProperty("session_id").GetString());
    }

    // ---------------------------------------------------------------- S71 commands

    [Fact]
    public async Task S71_tool_prints_the_status_line_and_the_data_as_one_line_of_json()
    {
        var run = await ScriptAsync(new[] { "/tool inspect_scene {}" });

        Assert.Equal(0, run.Code);
        Assert.Contains("[tool] inspect_scene success rev=0", run.Lines);
        var data = Assert.Single(run.Lines, l => l.StartsWith("{", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(data);
        Assert.Equal(0, doc.RootElement.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task S72_a_failing_tool_prints_its_error_code_and_is_a_normal_outcome()
    {
        var run = await ScriptAsync(new[] { "/tool no_such_tool {}", "/tool inspect_scene not-json" });

        Assert.Equal(0, run.Code);
        Assert.Contains("[tool] no_such_tool failure unknown_tool rev=0", run.Lines);
        Assert.Contains("[tool] inspect_scene failure invalid_arguments rev=0", run.Lines);
    }

    [Fact]
    public async Task S72_trace_does_not_print_a_tool_command_twice()
    {
        var run = await ScriptAsync(new[] { "/tool inspect_scene {}" }, extra: new[] { "--trace" });
        Assert.Single(run.Lines, l => l.StartsWith("[tool] inspect_scene", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S71_the_first_tool_after_startup_or_an_utterance_begins_a_new_manual_turn()
    {
        var chat = new ScriptedChatClient().EnqueueText("hello");
        var run = await ScriptAsync(new[]
        {
            "/tool bad_one {}", "/tool bad_two {}", "/tool bad_three {}",   // one manual turn: the 2nd failure exhausts it
            "an utterance",
            "/tool bad_four {}",                                            // a new manual turn: fresh budget
        }, chat);

        var tools = run.Lines.Where(l => l.StartsWith("[tool]", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[]
        {
            "[tool] bad_one failure unknown_tool rev=0",
            "[tool] bad_two failure unknown_tool rev=0",
            "[tool] bad_three failure turn_budget_exhausted rev=0",
            "[tool] bad_four failure unknown_tool rev=0",
        }, tools);
    }

    [Fact]
    public async Task S71_scene_lists_the_objects_and_the_selection()
    {
        var run = await ScriptAsync(new[] { Spawn("apple", "bag"), "/select apple_1", "/scene" });

        Assert.Contains("Scene rev 1, selected: apple_1", run.Lines);
        Assert.Contains("apple_1 | apple | center | 1 | - | りんご | りんご | apple", run.Lines);
        Assert.Contains(run.Lines, l => l.StartsWith("bag_1 | bag | center |", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S71_an_empty_room_is_said_to_be_empty()
    {
        var run = await ScriptAsync(new[] { "/scene" });
        // S128: the header line is still printed and is followed by the empty-room line.
        var at = run.Lines.ToList().IndexOf("Scene rev 0, selected: none");
        Assert.True(at >= 0);
        Assert.Equal(SceneColumnHeader, run.Lines[at + 1]);
        Assert.Equal("(the room is empty)", run.Lines[at + 2]);
    }

    [Fact]
    public async Task S71_select_and_move_act_as_the_player_and_change_the_scene()
    {
        var run = await ScriptAsync(new[]
        {
            Spawn("apple", "bag"),
            "/select apple_1",
            "/move apple_1 left",
            "/move apple_1 bag_1",
            "/scene",
        });

        Assert.Equal(0, run.Code);
        Assert.Contains("selected apple_1 (rev 1)", run.Lines);
        Assert.Contains("moved apple_1 to left (rev 2)", run.Lines);
        Assert.Contains("moved apple_1 to bag_1 (rev 3)", run.Lines);
        Assert.Contains(run.Lines, l => l.StartsWith("apple_1 | apple | center | ", StringComparison.Ordinal) && l.Contains("| bag_1 |"));
    }

    [Fact]
    public async Task S71_a_refused_move_prints_the_engine_error_and_is_not_a_command_error()
    {
        var run = await ScriptAsync(new[] { Spawn("apple"), "/move apple_1 center" });

        Assert.Contains(run.Lines, l => l.StartsWith("error: invalid_destination:", StringComparison.Ordinal));
        Assert.Equal(0, run.Code);
    }

    [Fact]
    public async Task S71_band_pref_and_profile_work_together()
    {
        var run = await ScriptAsync(new[] { "/band intermediate", "/pref pace slow", "/profile" });

        Assert.Equal(0, run.Code);
        Assert.Contains(run.Lines, l => l.StartsWith("band set to intermediate:", StringComparison.Ordinal));
        Assert.Contains("preference pace=slow (pinned)", run.Lines);
        Assert.Contains("starting band: intermediate", run.Lines);
        Assert.Contains("working level: n3 (hidden from learner)", run.Lines);
        Assert.Contains("pinned: pace", run.Lines);
        Assert.Contains(run.Lines, l => l.StartsWith("support: ", StringComparison.Ordinal) && l.Contains("pace=slow"));
    }

    [Fact]
    public async Task S71_profile_of_a_new_learner_shows_defaults()
    {
        var run = await ScriptAsync(new[] { "/profile" });
        Assert.Contains("starting band: not chosen", run.Lines);
        Assert.Contains("working level: pre_n5 (hidden from learner)", run.Lines);
        Assert.Contains("pinned: none", run.Lines);
        Assert.Contains("labels: none", run.Lines);
        Assert.Contains("notes: none", run.Lines);
    }

    [Fact]
    public async Task S71_summary_prints_the_session_summary_as_json_without_the_working_level()
    {
        var run = await ScriptAsync(new[] { "/band advanced", "/summary" });

        var line = Assert.Single(run.Lines, l => l.StartsWith("summary: ", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(line["summary: ".Length..]);
        Assert.StartsWith("s-", doc.RootElement.GetProperty("session_id").GetString());
        Assert.DoesNotContain("working_level", line);
        Assert.Equal("starting_band", doc.RootElement.GetProperty("preference_changes")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task S71_reset_clears_the_scene_and_the_profile()
    {
        var run = await ScriptAsync(new[] { Spawn("apple"), "/band advanced", "/reset", "/scene", "/profile" });

        Assert.Contains("profile reset", run.Lines);
        var at = run.Lines.ToList().FindIndex(l => l.StartsWith("Scene rev ", StringComparison.Ordinal) && l.EndsWith("selected: none", StringComparison.Ordinal));
        Assert.True(at >= 0);
        Assert.Equal(SceneColumnHeader, run.Lines[at + 1]);
        Assert.Equal("(the room is empty)", run.Lines[at + 2]);
        Assert.Contains("starting band: not chosen", run.Lines);
    }

    [Fact]
    public async Task S71_cancel_prints_cancelled_and_quit_ends_the_script()
    {
        var run = await ScriptAsync(new[] { "/cancel", "/quit", "/scene" });
        Assert.Contains("cancelled", run.Lines);
        Assert.DoesNotContain(run.Lines, l => l.StartsWith("Scene rev", StringComparison.Ordinal));
        Assert.Equal(0, run.Code);
    }

    [Fact]
    public async Task S71_help_lists_every_command()
    {
        var run = await ScriptAsync(new[] { "/help" });
        foreach (var command in new[] { "/help", "/scene", "/select", "/move", "/tool", "/band", "/pref", "/profile", "/summary", "/correct", "/cancel", "/reset", "/quit" })
            Assert.Contains(run.Lines, l => l.StartsWith(command, StringComparison.Ordinal));
    }

    [Fact]
    public async Task S71_commands_are_case_insensitive_and_blank_lines_are_skipped()
    {
        var run = await ScriptAsync(new[] { "", "   ", "/SCENE" });
        Assert.Contains("Scene rev 0, selected: none", run.Lines);
        Assert.Equal(0, run.Code);
    }

    [Fact]
    public async Task S48_correct_runs_a_new_turn_with_the_corrected_text()
    {
        var chat = new ScriptedChatClient().EnqueueText("a").EnqueueText("b");
        var run = await ScriptAsync(new[] { "I wnat water", "/correct I want water" }, chat);

        Assert.Equal(0, run.Code);
        Assert.Equal(2, run.ResultLines.Count());
        Assert.Contains("corrected from \"I wnat water\" to \"I want water\"", LearnerBlock(chat.Requests[1]));
    }

    [Fact]
    public async Task S99_correct_with_nothing_to_correct_is_an_error()
    {
        var run = await ScriptAsync(new[] { "/correct hello" }, new ScriptedChatClient().EnqueueText("x"));
        Assert.Contains(run.Lines, l => l.StartsWith("error: nothing_to_correct:", StringComparison.Ordinal));
        // S110 lists what exits 1 and what exits 2; this is neither, so it exits 0.
        Assert.Equal(0, run.Code);
    }

    // ---------------------------------------------------------------- S74 / S110 exit codes

    [Fact]
    public async Task S74_without_a_key_the_cli_starts_and_every_slash_command_works()
    {
        var run = await ScriptAsync(new[] { "/scene", "/profile", "/summary", "/tool inspect_scene {}" });
        Assert.Equal(0, run.Code);
        Assert.Contains("Scene rev 0, selected: none", run.Lines);
    }

    [Fact]
    public async Task S74_an_utterance_without_a_key_prints_the_error_line_and_exits_2()
    {
        var run = await ScriptAsync(new[] { "hello" });
        Assert.Contains("error: SENSEI_LLM_API_KEY is not set", run.Lines);
        Assert.Equal(2, run.Code);
        Assert.Matches(ResultPattern, Assert.Single(run.ResultLines));
    }

    [Fact]
    public async Task S74_slash_commands_still_work_after_the_failed_utterance()
    {
        var run = await ScriptAsync(new[] { "hello", "/scene" });
        Assert.Contains("Scene rev 0, selected: none", run.Lines);
        Assert.Equal(2, run.Code);
    }

    [Fact]
    public async Task S74_an_invalid_provider_setting_is_reported_for_utterances_and_the_cli_still_starts()
    {
        var env = new Dictionary<string, string?> { ["SENSEI_LLM_PROVIDER"] = "mystery", ["SENSEI_LLM_API_KEY"] = "k-1234567890" };
        var run = await ScriptAsync(new[] { "/scene", "hello" }, env: env);
        Assert.Contains(run.Lines, l => l.StartsWith("error: unknown SENSEI_LLM_PROVIDER 'mystery'; allowed values: openrouter, meta, openai_compatible", StringComparison.Ordinal));
        Assert.Equal(2, run.Code);
    }

    [Fact]
    public async Task S110_a_script_with_no_problems_exits_0()
    {
        var chat = new ScriptedChatClient().EnqueueText("fine");
        var run = await ScriptAsync(new[] { "hello", "/scene", "/tool no_such_tool {}" }, chat);
        Assert.Equal(0, run.Code);
    }

    [Fact]
    public async Task S110_cancelled_and_budget_exhausted_turns_are_normal_outcomes()
    {
        var chat = new ScriptedChatClient();
        for (var i = 0; i < 5; i++) chat.EnqueueFunctionCalls(($"c{i}", "no_such_tool", "{}"));
        var run = await ScriptAsync(new[] { "hello" }, chat);

        Assert.Equal(0, run.Code);
        Assert.Contains(run.ResultLines, l => l.Contains("budget_exhausted") && l.Contains("4 rounds"));
    }

    [Fact]
    public async Task S110_a_provider_error_exits_2_and_prints_the_code()
    {
        var chat = new ScriptedChatClient().EnqueueError(new ClientResultException("denied", new FakePipelineResponse(401)));
        var run = await ScriptAsync(new[] { "hello" }, chat);

        Assert.Equal(2, run.Code);
        Assert.Contains(run.Lines, l => l.StartsWith("error: auth_failed:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(429, "quota_exceeded")]
    [InlineData(402, "quota_exceeded")]
    [InlineData(500, "provider_error")]
    public async Task S110_every_provider_failure_code_exits_2(int status, string code)
    {
        var chat = new ScriptedChatClient().EnqueueError(new ClientResultException("failed", new FakePipelineResponse(status)));
        var run = await ScriptAsync(new[] { "hello" }, chat);
        Assert.Equal(2, run.Code);
        Assert.Contains(run.Lines, l => l.StartsWith("error: " + code + ":", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S110_an_unknown_command_exits_1_even_after_an_llm_failure()
    {
        var run = await ScriptAsync(new[] { "hello", "/bogus" });
        Assert.Equal(1, run.Code);
        Assert.Contains("error: unknown command '/bogus' (try /help)", run.Lines);
    }

    [Theory]
    [InlineData("/band nonsense")]
    [InlineData("/band")]
    [InlineData("/pref nothing true")]
    [InlineData("/pref pace fast")]
    [InlineData("/pref show_romaji maybe")]
    [InlineData("/pref pace")]
    [InlineData("/select")]
    [InlineData("/select missing_1")]
    [InlineData("/move apple_1")]
    [InlineData("/move missing_1 left")]
    [InlineData("/tool")]
    [InlineData("/correct")]
    public async Task S110_invalid_command_arguments_exit_1(string line)
    {
        var run = await ScriptAsync(new[] { line });
        Assert.Equal(1, run.Code);
        Assert.Contains(run.Lines, l => l.StartsWith("error: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S110_a_bad_script_file_exits_1()
    {
        var writer = new StringWriter();
        var code = await Sensei.Engine.Cli.CliApp.RunAsync(new[] { "--profile-dir", CliRunner.NewTempDirectory(), "--script", "/no/such/file.txt" }, TextReader.Null, writer, NoEnv);
        Assert.Equal(1, code);
        Assert.Contains("error: cannot read script", writer.ToString());
    }

    [Fact]
    public async Task S110_bad_options_exit_1_with_the_usage_text()
    {
        var writer = new StringWriter();
        var code = await Sensei.Engine.Cli.CliApp.RunAsync(new[] { "--nope" }, TextReader.Null, writer, NoEnv);
        Assert.Equal(1, code);
        Assert.Contains("error: unknown option '--nope'", writer.ToString());
        Assert.Contains("usage: sensei", writer.ToString());
    }

    [Fact]
    public async Task S13_help_prints_the_usage_and_exits_0()
    {
        var writer = new StringWriter();
        var code = await Sensei.Engine.Cli.CliApp.RunAsync(new[] { "--help" }, TextReader.Null, writer, NoEnv);
        Assert.Equal(0, code);
        Assert.Contains("--profile-dir", writer.ToString());
    }

    [Fact]
    public async Task S57_an_invalid_profile_id_exits_1()
    {
        var run = await ScriptAsync(new[] { "/scene" }, extra: new[] { "--profile", "Bad Id" });
        Assert.Equal(1, run.Code);
        Assert.Contains(run.Lines, l => l.StartsWith("error: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S57_a_corrupt_profile_file_exits_1_and_is_left_untouched()
    {
        var dir = CliRunner.NewTempDirectory();
        var file = Path.Combine(dir, "default.json");
        await File.WriteAllTextAsync(file, "{ not json");

        var run = await ScriptAsync(new[] { "/scene" }, profileDir: dir);

        Assert.Equal(1, run.Code);
        Assert.Equal("{ not json", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task S59_a_second_run_on_the_same_profile_restores_band_and_working_level()
    {
        var dir = CliRunner.NewTempDirectory();
        await ScriptAsync(new[] { "/band advanced", "/pref pace slow" }, profileDir: dir);

        var second = await ScriptAsync(new[] { "/profile" }, profileDir: dir);

        Assert.Contains("starting band: advanced", second.Lines);
        Assert.Contains("working level: n2 (hidden from learner)", second.Lines);
        Assert.Contains("pinned: pace", second.Lines);
    }

    [Fact]
    public async Task S57_profile_selects_the_file_and_keeps_learners_apart()
    {
        var dir = CliRunner.NewTempDirectory();
        await ScriptAsync(new[] { "/band advanced" }, profileDir: dir, extra: new[] { "--profile", "kid1" });

        var other = await ScriptAsync(new[] { "/profile" }, profileDir: dir, extra: new[] { "--profile", "kid2" });

        Assert.True(File.Exists(Path.Combine(dir, "kid1.json")));
        Assert.Contains("starting band: not chosen", other.Lines);
    }

    // ---------------------------------------------------------------- S77 secrets

    [Fact]
    public async Task S77_the_api_key_is_redacted_from_error_lines()
    {
        const string Key = "very-secret-key-0123456789";
        var env = new Dictionary<string, string?> { ["SENSEI_LLM_API_KEY"] = Key };
        var chat = new ScriptedChatClient().EnqueueError(new InvalidOperationException("request rejected for " + Key));
        var run = await ScriptAsync(new[] { "hello", "/tool no_such_tool {}" }, chat, env);

        Assert.DoesNotContain(Key, run.Text);
        Assert.Contains("[redacted]", run.Text);
    }

    [Fact]
    public async Task S77_the_api_key_never_reaches_the_trace_file_or_the_model()
    {
        const string Key = "very-secret-key-0123456789";
        var env = new Dictionary<string, string?> { ["SENSEI_LLM_API_KEY"] = Key };
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", Key, "{}")).EnqueueText("done");
        var run = await ScriptAsync(new[] { "my key is " + Key }, chat, env);

        foreach (var file in Directory.GetFiles(run.ProfileDir, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(Key, await File.ReadAllTextAsync(file));
        foreach (var request in chat.Requests) Assert.DoesNotContain(Key, Everything(request));
        Assert.DoesNotContain(Key, run.Text);
    }

    // ---------------------------------------------------------------- script mode specifics

    [Fact]
    public async Task S109_script_mode_runs_each_line_to_completion_before_the_next()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "speak", SpeakJson)).EnqueueText("done")
            .EnqueueText("second");
        var run = await ScriptAsync(new[] { "first", "/scene", "second" }, chat);

        var firstResult = run.IndexOf(l => l.StartsWith("Sensei: ", StringComparison.Ordinal));
        var scene = run.IndexOf(l => l.StartsWith("Scene rev", StringComparison.Ordinal));
        Assert.True(firstResult >= 0 && firstResult < scene, string.Join("\n", run.Lines));
        Assert.Equal(2, run.ResultLines.Count());
    }

    [Fact]
    public async Task S109_script_mode_prints_no_banner()
    {
        var run = await ScriptAsync(new[] { "/scene" });
        Assert.DoesNotContain(run.Lines, l => l.StartsWith("Red Panda Sensei", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- S109 interactive mode

    /// <summary>Hangs until cancelled when the current learner line is exactly <paramref name="hangsOn"/> (not when a later block merely quotes it).</summary>
    private static FuncChatClient SlowFirstClient(string hangsOn, string answer = "ok") => new(async (messages, _, ct) =>
    {
        var learner = messages.Last(m => m.Role == ChatRole.User).Text;
        if (learner.Replace("\r", "").Split('\n').Contains("learner_transcript: " + hangsOn)) await Task.Delay(Timeout.Infinite, ct);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));
    });

    [Fact]
    public async Task S109_commands_act_while_a_turn_is_running()
    {
        var chat = SlowFirstClient("slow one");
        var run = await InteractiveAsync(new StringReader("slow one\n/scene\n/quit\n"), chat);

        var scene = run.IndexOf(l => l.StartsWith("Scene rev", StringComparison.Ordinal));
        var interrupted = run.IndexOf(l => l.StartsWith("Sensei: interrupted", StringComparison.Ordinal));
        Assert.True(scene >= 0 && interrupted > scene, string.Join("\n", run.Lines));
    }

    [Fact]
    public async Task S109_a_new_utterance_replaces_the_running_turn()
    {
        var chat = SlowFirstClient("slow one", "second answer");
        var run = await InteractiveAsync(new StringReader("slow one\nfast two\n"), chat);

        Assert.Equal(2, run.ResultLines.Count());
        Assert.Contains(run.ResultLines, l => l.Contains("turn 1: cancelled"));
        Assert.Contains(run.ResultLines, l => l.Contains("turn 2: completed"));
        Assert.Equal(0, run.Code);
    }

    [Fact]
    public async Task S109_cancel_stops_the_running_turn_and_input_continues()
    {
        var chat = SlowFirstClient("slow one");
        var run = await InteractiveAsync(new StringReader("slow one\n/cancel\n/scene\n"), chat);

        Assert.Contains("cancelled", run.Lines);
        Assert.Contains(run.ResultLines, l => l.Contains("turn 1: cancelled"));
        Assert.Contains(run.Lines, l => l.StartsWith("Scene rev", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S109_end_of_input_waits_for_the_running_turn_to_finish()
    {
        var chat = new FuncChatClient(async (_, _, ct) =>
        {
            await Task.Delay(300, ct);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "late answer"));
        });
        var run = await InteractiveAsync(new StringReader("hello\n"), chat);

        Assert.Contains(run.ResultLines, l => l.Contains("turn 1: completed"));
    }

    [Fact]
    public async Task S109_interactive_mode_greets_with_the_session_and_always_exits_0_for_command_errors()
    {
        var run = await InteractiveAsync(new StringReader("/bogus\n"));
        Assert.StartsWith("Red Panda Sensei - session s-", run.Lines[0]);
        Assert.Contains("error: unknown command '/bogus' (try /help)", run.Lines);
        Assert.Equal(0, run.Code);
    }

    [Fact]
    public async Task S109_tool_during_a_running_turn_reports_the_turn_in_progress()
    {
        var chat = SlowFirstClient("slow one");
        var run = await InteractiveAsync(new StringReader("slow one\n/tool inspect_scene {}\n/quit\n"), chat);
        Assert.Contains(run.Lines, l => l.StartsWith("error: ", StringComparison.Ordinal) && l.Contains("turn_in_progress"));
    }
}

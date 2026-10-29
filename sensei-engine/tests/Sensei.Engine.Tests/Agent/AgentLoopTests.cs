using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Events;
using Sensei.Engine.Tests.Foundation;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Agent;

public sealed class AgentLoopTests
{
    // ---------------------------------------------------------------- S61 stack and tools

    [Fact]
    public async Task S61_the_agent_offers_the_16_tools_in_spec_order_with_explicit_schemas()
    {
        var chat = new ScriptedChatClient().EnqueueText("ok");
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("hello");

        var tools = chat.Requests[0].Options!.Tools!;
        Assert.Equal(t.Engine.ToolNames.Where(n => n != "update_challenge"), tools.Select(x => x.Name).ToArray());
        Assert.Equal(15, tools.Count);
        foreach (var tool in tools)
        {
            var function = Assert.IsAssignableFrom<SenseiTool>(tool);
            Assert.Equal(System.Text.Json.JsonValueKind.Object, function.JsonSchema.ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(function.Description));
        }
    }

    [Fact]
    public void S61_the_context_provider_is_an_agent_framework_ai_context_provider()
    {
        Assert.True(typeof(AIContextProvider).IsAssignableFrom(typeof(SenseiContextProvider)));
        Assert.True(typeof(IChatClient).IsAssignableFrom(typeof(RoundLimitingChatClient)));
    }

    [Fact]
    public async Task S61_calls_of_one_response_are_dispatched_in_order_and_each_result_goes_back()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "a")), ("c2", "test_probe", ProbeJson("ok", "b")), ("c3", "test_probe", ProbeJson("ok", "c")))
            .EnqueueText("done");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal(TurnStatuses.Completed, result.Status);
        Assert.Equal(new[] { "c1", "c2", "c3" }, result.ToolCalls.Select(c => c.CallId).ToArray());
        Assert.Equal(new[] { "c1", "c2", "c3" }, trace.Entries.Select(c => c.CallId).ToArray());
        var results = ResultsIn(chat.Requests[1]);
        Assert.Equal(new[] { "c1", "c2", "c3" }, results.Select(r => r.CallId).ToArray());
        Assert.All(results, r => Assert.Equal("success", StatusOf(r)));
        Assert.Equal(new[] { "a", "b", "c" }, results.Select(r => ResultJson(r).GetProperty("data").GetProperty("echo").GetString()).ToArray());
    }

    [Fact]
    public async Task S61_calls_of_one_response_never_run_at_the_same_time()
    {
        var timed = new TimedProbeTool();
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_timed", ProbeJson("ok", "first")), ("c2", "test_timed", ProbeJson("ok", "second")), ("c3", "test_timed", ProbeJson("ok", "third")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat, probes: false, more: timed);

        await t.Engine.RunTurnAsync("go");

        Assert.Equal(new[] { "first", "second", "third" }, timed.Spans.Select(s => s.Text).ToArray());
        for (var i = 1; i < timed.Spans.Count; i++)
            Assert.True(timed.Spans[i].Start >= timed.Spans[i - 1].End, $"call {i} started before call {i - 1} ended");
    }

    [Fact]
    public async Task S61_the_result_the_model_receives_is_the_engine_tool_result_json()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("call_x", "test_probe", ProbeJson("fail_unknown"))).EnqueueText("done");
        await using var t = await EngineAsync(chat);
        var result = await t.Engine.RunTurnAsync("go");

        var json = ResultJson(Assert.Single(ResultsIn(chat.Requests[1])));
        Assert.Equal("call_x", json.GetProperty("call_id").GetString());
        Assert.Equal("test_probe", json.GetProperty("tool").GetString());
        Assert.Equal("failure", json.GetProperty("status").GetString());
        Assert.Equal("unknown_object", json.GetProperty("error_code").GetString());
        Assert.Equal("probe failure", json.GetProperty("detail").GetString());
        Assert.Equal(result.ToolCalls[0].Result!.Revision, json.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task S61_each_turn_gets_its_own_turn_id_and_generation()
    {
        var chat = new ScriptedChatClient().EnqueueText("a").EnqueueText("b");
        await using var t = await EngineAsync(chat);
        var first = await t.Engine.RunTurnAsync("one");
        var second = await t.Engine.RunTurnAsync("two");
        Assert.Equal(1, first.TurnId);
        Assert.Equal(2, second.TurnId);
        Assert.True(second.GenerationId > first.GenerationId);
    }

    // ---------------------------------------------------------------- S64 rounds

    [Fact]
    public async Task S64_the_fifth_model_request_never_reaches_the_provider()
    {
        var chat = new ScriptedChatClient();
        for (var i = 1; i <= 5; i++) chat.EnqueueFunctionCalls(($"c{i}", "test_probe", ProbeJson("ok", "x")));
        await using var t = await EngineAsync(chat);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal(4, chat.RequestCount);
        Assert.Equal(1, chat.Remaining);
        Assert.Equal(TurnStatuses.BudgetExhausted, result.Status);
        Assert.Equal(4, result.Rounds);
        Assert.Null(result.ErrorCode);
        Assert.Equal(4, result.ToolCalls.Count);
    }

    [Fact]
    public async Task S64_four_rounds_that_end_in_text_complete_normally()
    {
        var chat = new ScriptedChatClient();
        for (var i = 1; i <= 3; i++) chat.EnqueueFunctionCalls(($"c{i}", "test_probe", ProbeJson("ok", "x")));
        chat.EnqueueText("finished");
        await using var t = await EngineAsync(chat);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal(4, chat.RequestCount);
        Assert.Equal(TurnStatuses.Completed, result.Status);
        Assert.Equal(4, result.Rounds);
        Assert.Equal("finished", result.UnspokenText);
    }

    [Fact]
    public async Task S64_the_round_limit_applies_to_each_turn_separately()
    {
        var chat = new ScriptedChatClient();
        for (var i = 1; i <= 8; i++) chat.EnqueueFunctionCalls(($"c{i}", "test_probe", ProbeJson("ok", "x")));
        await using var t = await EngineAsync(chat);

        var first = await t.Engine.RunTurnAsync("one");
        var second = await t.Engine.RunTurnAsync("two");

        Assert.Equal(TurnStatuses.BudgetExhausted, first.Status);
        Assert.Equal(TurnStatuses.BudgetExhausted, second.Status);
        Assert.Equal(8, chat.RequestCount);
        Assert.Equal(4, second.Rounds);
    }

    [Fact]
    public async Task S64_the_empty_answer_of_the_limiter_is_not_sent_back_as_history()
    {
        var chat = new ScriptedChatClient();
        for (var i = 1; i <= 5; i++) chat.EnqueueFunctionCalls(($"c{i}", "test_probe", ProbeJson("ok", "x")));
        chat.EnqueueText("next turn answer");
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("one");

        // the 5th scripted answer was never used; drain it so the next turn gets the text
        chat.Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "second turn"))));
        await t.Engine.RunTurnAsync("two");

        var request = chat.Requests.Last();
        Assert.DoesNotContain(request.Messages, m => m.Role == ChatRole.Assistant && !m.Contents.OfType<FunctionCallContent>().Any() && string.IsNullOrWhiteSpace(m.Text));
    }

    [Fact]
    public async Task S64_the_round_number_in_the_trace_follows_the_model_round()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "a")))
            .EnqueueFunctionCalls(("c2", "test_probe", ProbeJson("ok", "b")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal(new[] { 1, 2 }, result.ToolCalls.Select(c => c.Round).ToArray());
        Assert.Equal(3, result.Rounds);
    }

    [Fact]
    public async Task S64_a_text_only_answer_is_one_round()
    {
        var chat = new ScriptedChatClient().EnqueueText("hi");
        await using var t = await EngineAsync(chat);
        var result = await t.Engine.RunTurnAsync("go");
        Assert.Equal(1, result.Rounds);
        Assert.Equal(TurnStatuses.Completed, result.Status);
    }

    // ---------------------------------------------------------------- S26 / S84 through the loop

    [Fact]
    public async Task S84_an_id_spawned_in_a_round_is_usable_only_from_the_next_round()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("spawn")), ("c2", "test_probe", ProbeJson("ok", idsJson: "[\"apple_1\"]")))
            .EnqueueFunctionCalls(("c3", "test_probe", ProbeJson("ok", idsJson: "[\"apple_1\"]")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat);

        await t.Engine.RunTurnAsync("go");

        var results = ResultsIn(chat.Requests[2]);
        Assert.Equal(new[] { "c1", "c2", "c3" }, results.Select(r => r.CallId).ToArray());
        Assert.Equal("success", StatusOf(results[0]));
        Assert.Equal("invalid_arguments", ErrorCodeOf(results[1]));
        Assert.Contains("id_not_yet_visible", DetailOf(results[1]));
        Assert.Equal("success", StatusOf(results[2]));
    }

    [Fact]
    public async Task S26_a_player_move_during_the_model_request_makes_mutating_calls_stale_until_the_next_round()
    {
        TestEngine? holder = null;
        var chat = new ScriptedChatClient();
        chat.Enqueue((_, _) =>
        {
            holder!.Engine.PlayerMove("apple_1", "left", null);
            return Task.FromResult(CallsResponse(("c1", "test_mutate", ProbeJson("ok"))));
        });
        chat.EnqueueFunctionCalls(("c2", "test_mutate", ProbeJson("ok")));
        chat.EnqueueText("done");
        await using var t = await EngineAsync(chat);
        holder = t;
        SceneHelpers.Spawn(t, "apple");

        await t.Engine.RunTurnAsync("go");

        Assert.Equal("stale_scene", ErrorCodeOf(ResultsIn(chat.Requests[1]).Single()));
        Assert.Equal("success", StatusOf(ResultsIn(chat.Requests[2]).Last()));
    }

    // ---------------------------------------------------------------- S85 every provider call goes through the pipeline

    [Fact]
    public async Task S85_an_unknown_tool_name_gets_one_engine_result_and_is_traced()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "does_not_exist", "{\"x\":1}")).EnqueueText("done");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);

        var result = await t.Engine.RunTurnAsync("go");

        var back = Assert.Single(ResultsIn(chat.Requests[1]));
        Assert.Equal("c1", back.CallId);
        Assert.Equal("failure", StatusOf(back));
        Assert.Equal("unknown_tool", ErrorCodeOf(back));
        var entry = Assert.Single(trace.Entries);
        Assert.Equal("does_not_exist", entry.Tool);
        Assert.Equal("unknown_tool", entry.ErrorCode);
        Assert.Equal("c1", entry.CallId);
        Assert.Single(result.ToolCalls);
    }

    [Fact]
    public async Task S85_the_framework_error_text_for_unknown_functions_is_never_what_the_model_sees()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "does_not_exist", "{}")).EnqueueText("done");
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("go");

        var back = Assert.Single(ResultsIn(chat.Requests[1]));
        Assert.IsType<System.Text.Json.JsonElement>(back.Result);
        Assert.DoesNotContain("not found", back.Result!.ToString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task S85_arguments_the_provider_could_not_parse_are_answered_by_the_pipeline_and_the_tool_does_not_run()
    {
        var probe = new ProbeTool();
        var chat = new ScriptedChatClient();
        chat.Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("c1", "test_probe", null) { Exception = new InvalidOperationException("Error parsing function call arguments.") },
        }))));
        chat.EnqueueText("done");
        await using var t = await EngineAsync(chat, probes: false, more: probe);

        await t.Engine.RunTurnAsync("go");

        var back = Assert.Single(ResultsIn(chat.Requests[1]));
        Assert.Equal("invalid_arguments", ErrorCodeOf(back));
        Assert.True(DetailOf(back).Contains("malformed", StringComparison.OrdinalIgnoreCase), "detail was: " + DetailOf(back));
        Assert.Equal(0, probe.Executions);
    }

    [Fact]
    public async Task S85_a_known_tool_with_unparseable_arguments_is_a_counted_validation_failure()
    {
        var chat = new ScriptedChatClient();
        chat.Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("c1", "inspect_scene", null) { Exception = new InvalidOperationException("bad") },
            new FunctionCallContent("c2", "inspect_scene", null) { Exception = new InvalidOperationException("bad") },
            new FunctionCallContent("c3", "test_probe", ScriptedChatClient.ParseArguments(ProbeJson("ok"))),
        }))));
        chat.EnqueueText("done");
        await using var t = await EngineAsync(chat);

        await t.Engine.RunTurnAsync("go");

        var results = ResultsIn(chat.Requests[1]);
        Assert.Equal("invalid_arguments", ErrorCodeOf(results[0]));
        Assert.Equal("invalid_arguments", ErrorCodeOf(results[1]));
        Assert.Equal("turn_budget_exhausted", ErrorCodeOf(results[2]));
    }

    [Fact]
    public async Task S85_known_unknown_and_unparseable_calls_each_produce_exactly_one_result_in_call_order()
    {
        var chat = new ScriptedChatClient();
        chat.Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("c1", "test_probe", ScriptedChatClient.ParseArguments(ProbeJson("ok", "a"))),
            new FunctionCallContent("c2", "ghost_tool", ScriptedChatClient.ParseArguments("{}")),
            new FunctionCallContent("c3", "test_probe", null) { Exception = new InvalidOperationException("bad") },
        }))));
        chat.EnqueueText("done");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);

        var result = await t.Engine.RunTurnAsync("go");

        var results = ResultsIn(chat.Requests[1]);
        Assert.Equal(new[] { "c1", "c2", "c3" }, results.Select(r => r.CallId).ToArray());
        Assert.Equal(new[] { "success", "failure", "failure" }, results.Select(StatusOf).ToArray());
        Assert.Equal(new[] { "c1", "c2", "c3" }, trace.Entries.Select(e => e.CallId).ToArray());
        Assert.Equal(3, result.ToolCalls.Count);
    }

    [Fact]
    public async Task S85_two_unknown_tools_exhaust_the_turn_for_later_calls()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "ghost_a", "{}"), ("c2", "ghost_b", "{}"), ("c3", "test_probe", ProbeJson("ok")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat);

        await t.Engine.RunTurnAsync("go");

        var results = ResultsIn(chat.Requests[1]);
        Assert.Equal("unknown_tool", ErrorCodeOf(results[0]));
        Assert.Equal("unknown_tool", ErrorCodeOf(results[1]));
        Assert.Equal("turn_budget_exhausted", ErrorCodeOf(results[2]));
    }

    [Fact]
    public async Task S85_the_budget_of_eight_calls_applies_to_provider_calls()
    {
        var chat = new ScriptedChatClient();
        chat.EnqueueFunctionCalls(Enumerable.Range(1, 9).Select(i => ($"c{i}", "test_probe", ProbeJson("ok", "x"))).ToArray());
        chat.EnqueueText("done");
        await using var t = await EngineAsync(chat);

        await t.Engine.RunTurnAsync("go");

        var results = ResultsIn(chat.Requests[1]);
        Assert.Equal(9, results.Count);
        Assert.All(results.Take(8), r => Assert.Equal("success", StatusOf(r)));
        Assert.Equal("turn_budget_exhausted", ErrorCodeOf(results[8]));
    }

    [Fact]
    public async Task S85_the_same_call_id_and_arguments_twice_is_a_cached_duplicate_that_runs_once()
    {
        var probe = new ProbeTool();
        var trace = new RecordingTraceSink();
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "a")), ("c1", "test_probe", ProbeJson("ok", "a")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat, probes: false, trace: trace, more: probe);

        await t.Engine.RunTurnAsync("go");

        Assert.Equal(1, probe.Executions);
        Assert.Equal(new[] { false, true }, trace.Entries.Select(e => e.Duplicate).ToArray());
    }

    [Fact]
    public async Task S85_a_tool_that_throws_still_gets_one_result_and_the_turn_continues()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("throw")), ("c2", "test_probe", ProbeJson("ok", "b")))
            .EnqueueText("done");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal(TurnStatuses.Completed, result.Status);
        var results = ResultsIn(chat.Requests[1]);
        Assert.Equal("internal_error", ErrorCodeOf(results[0]));
        Assert.Equal("success", StatusOf(results[1]));
        Assert.Contains(trace.Entries, e => e.CallId == "c1" && e.ErrorCode == "internal_error");
    }

    [Fact]
    public async Task S85_an_empty_provider_call_id_gets_an_engine_assigned_id()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("", "test_probe", ProbeJson("ok", "a"))).EnqueueText("done");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);

        await t.Engine.RunTurnAsync("go");

        Assert.StartsWith("auto-", Assert.Single(trace.Entries).CallId);
    }

    // ---------------------------------------------------------------- S88 end of activity

    [Fact]
    public async Task S88_after_a_call_ends_the_generation_the_rest_of_the_turn_is_cancelled()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("end_generation")), ("c2", "test_probe", ProbeJson("ok", "a")))
            .EnqueueFunctionCalls(("c3", "test_probe", ProbeJson("ok", "b")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal("success", StatusOf(ResultsIn(chat.Requests[1])[0]));
        Assert.Equal("cancelled", StatusOf(ResultsIn(chat.Requests[1])[1]));
        Assert.Equal("cancelled", StatusOf(ResultsIn(chat.Requests[2]).Last()));
        Assert.Equal(TurnStatuses.Completed, result.Status);
    }

    // ---------------------------------------------------------------- S67 TurnResult and S69 events

    [Fact]
    public async Task S67_a_completed_turn_reports_ids_rounds_tokens_and_calls()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "a"))).EnqueueText("done");
        await using var t = await EngineAsync(chat);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal(1, result.TurnId);
        Assert.True(result.GenerationId > 0);
        Assert.Equal(TurnStatuses.Completed, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorDetail);
        Assert.Equal(2, result.Rounds);
        Assert.Equal(20, result.InputTokens);
        Assert.Equal(10, result.OutputTokens);
        Assert.Single(result.ToolCalls);
    }

    [Fact]
    public async Task S67_plain_assistant_text_is_kept_as_unspoken_text_and_never_spoken_or_shown()
    {
        var speech = new RecordingSpeechOutput();
        var events = new RecordingEvents();
        var chat = new ScriptedChatClient().EnqueueText("Konnichiwa, I will not be heard");
        await using var t = await EngineAsync(chat, speech: speech, events: events);

        var result = await t.Engine.RunTurnAsync("hi");

        Assert.Equal("Konnichiwa, I will not be heard", result.UnspokenText);
        Assert.Empty(speech.Requests);
        Assert.Empty(events.Hints);
        Assert.Empty(result.Spoken);
    }

    [Fact]
    public async Task S67_a_turn_without_assistant_text_has_no_unspoken_text()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "a"))).Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))));
        await using var t = await EngineAsync(chat);
        var result = await t.Engine.RunTurnAsync("go");
        Assert.Null(result.UnspokenText);
    }

    [Fact]
    public async Task S67_text_from_several_rounds_is_joined_in_order()
    {
        var chat = new ScriptedChatClient();
        chat.Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new TextContent("first thought"),
            new FunctionCallContent("c1", "test_probe", ScriptedChatClient.ParseArguments(ProbeJson("ok", "a"))),
        }))));
        chat.EnqueueText("second thought");
        await using var t = await EngineAsync(chat);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal("first thought\nsecond thought", result.UnspokenText);
    }

    [Fact]
    public async Task S67_spoken_lists_the_utterances_presented_in_this_turn_only()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_say", ProbeJson("ok", "こんにちは")), ("c2", "test_say", ProbeJson("ok", "ありがとう")))
            .EnqueueText("done")
            .EnqueueFunctionCalls(("c3", "test_say", ProbeJson("ok", "さようなら")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat, more: new SpeechProbeTool());

        var first = await t.Engine.RunTurnAsync("one");
        var second = await t.Engine.RunTurnAsync("two");

        Assert.Equal(new[] { "u1", "u2" }, first.Spoken.Select(s => s.UtteranceId).ToArray());
        Assert.Equal("こんにちは", first.Spoken[0].Segments[0].Text);
        Assert.Equal("success", first.Spoken[0].Outcome);
        Assert.Equal(new[] { "u3" }, second.Spoken.Select(s => s.UtteranceId).ToArray());
    }

    [Fact]
    public async Task S69_thinking_while_the_request_is_in_flight_and_idle_at_the_end()
    {
        var events = new RecordingEvents();
        var chat = new ScriptedChatClient().EnqueueText("hi");
        await using var t = await EngineAsync(chat, events: events);
        await t.Engine.RunTurnAsync("go");
        Assert.Equal(new[] { "thinking", "idle" }, events.SenseiStates.ToArray());
    }

    [Fact]
    public async Task S69_a_multi_round_turn_raises_thinking_once_per_change()
    {
        var events = new RecordingEvents();
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "a")))
            .EnqueueFunctionCalls(("c2", "test_probe", ProbeJson("ok", "b")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat, events: events);
        await t.Engine.RunTurnAsync("go");
        Assert.Equal(new[] { "thinking", "idle" }, events.SenseiStates.ToArray());
    }

    [Fact]
    public async Task S69_speaking_is_raised_around_speech_tools_and_thinking_resumes_for_the_next_request()
    {
        var events = new RecordingEvents();
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "speak", "{}")).EnqueueText("done");
        await using var t = await EngineAsync(chat, events: events);
        await t.Engine.RunTurnAsync("go");
        Assert.Equal(new[] { "thinking", "speaking", "thinking", "idle" }, events.SenseiStates.ToArray());
    }

    [Fact]
    public async Task S69_a_hint_without_speech_does_not_raise_speaking()
    {
        var events = new RecordingEvents();
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "show_hint", "{\"kind\":\"explanation\",\"content\":{\"english\":\"x\"}}")).EnqueueText("done");
        await using var t = await EngineAsync(chat, events: events);
        await t.Engine.RunTurnAsync("go");
        Assert.DoesNotContain("speaking", events.SenseiStates);
    }

    [Fact]
    public async Task S69_a_hint_that_speaks_raises_speaking()
    {
        var events = new RecordingEvents();
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "show_hint", "{\"kind\":\"explanation\",\"content\":{\"english\":\"x\"},\"speak\":true}")).EnqueueText("done");
        await using var t = await EngineAsync(chat, events: events);
        await t.Engine.RunTurnAsync("go");
        Assert.Contains("speaking", events.SenseiStates);
    }

    [Fact]
    public async Task S69_a_failed_turn_still_ends_idle()
    {
        var events = new RecordingEvents();
        var chat = new ScriptedChatClient().EnqueueError(new InvalidOperationException("boom"));
        await using var t = await EngineAsync(chat, events: events);
        var result = await t.Engine.RunTurnAsync("go");
        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal("idle", events.SenseiStates.Last());
    }

    [Fact]
    public async Task S67_a_turn_without_a_chat_client_fails_with_llm_not_configured_and_leaves_the_scene_alone()
    {
        await using var t = await TestEngine.CreateAsync();
        var revision = t.Engine.GetScene().Revision;
        var result = await t.Engine.RunTurnAsync("hi");
        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal("llm_not_configured", result.ErrorCode);
        Assert.Equal(revision, t.Engine.GetScene().Revision);
    }

    [Fact]
    public async Task S117_the_utterance_is_recorded_before_the_provider_is_called()
    {
        TestEngine? holder = null;
        var seen = new TaskCompletionSource<int>();
        var chat = new ScriptedChatClient();
        chat.Enqueue((_, _) =>
        {
            lock (holder!.Core.Sync) seen.TrySetResult(holder.Core.Utterances.All.Count);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        });
        await using var t = await EngineAsync(chat);
        holder = t;
        await t.Engine.RunTurnAsync("go");
        Assert.Equal(1, await seen.Task);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.Cli;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Integration.ReviewFinal2;

public sealed class ReviewFinal2Tests
{
    private const string Secret = "review-final2-key-QX42";

    [Fact]
    public async Task F01_loopback_oversized_json_element_arguments_must_be_replaced_and_traced()
    {
        var args = "{\"extra\":[" + string.Join(",", Enumerable.Repeat("1", 10001)) + "]}";
        using var server = new FakeChatServer().EnqueueToolCalls(("large", "inspect_scene", args)).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(chatClient: client, trace: trace);

        var turn = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(TurnStatuses.Completed, turn.Status);
        Assert.Equal(ToolErrorCodes.InvalidArguments, Assert.Single(turn.ToolCalls).ErrorCode);
        using var body = JsonDocument.Parse(server.Requests[1].Body);
        var call = body.RootElement.GetProperty("messages").EnumerateArray()
            .Where(m => m.TryGetProperty("tool_calls", out _)).Single().GetProperty("tool_calls")[0];
        Assert.Equal("{}", call.GetProperty("function").GetProperty("arguments").GetString());
        Assert.Contains(trace.Entries, e => e.Tool == "engine:outbound_content" && e.ErrorCode == "outbound_content_removed");
    }

    [Theory]
    [InlineData("element")]
    [InlineData("node")]
    public void F01_json_shaped_results_must_obey_the_same_node_limit(string shape)
    {
        var json = "[" + string.Join(",", Enumerable.Repeat("1", 10001)) + "]";
        object result = shape == "element" ? JsonDocument.Parse(json).RootElement.Clone() : JsonNode.Parse(json)!;
        var notes = new List<string>();

        var sent = OutboundMessages.Sanitized(new[] { new ChatMessage(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("r", result) }) }, new Secrets(new[] { Secret }), notes);

        var actual = Assert.Single(sent).Contents.OfType<FunctionResultContent>().Single().Result;
        Assert.True(actual is string text && text == "[oversized value]", "Expected whole result replacement; actual type=" + actual?.GetType().Name);
        Assert.NotEmpty(notes);
    }

    [Fact]
    public async Task F02_uri_argument_must_not_resend_the_known_key_to_a_fake_provider()
    {
        var call = new FunctionCallContent("uri", "inspect_scene", new Dictionary<string, object?> { ["extra"] = new Uri("https://example.com/" + Secret) });
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent> { call }))))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Secret });

        var turn = await t.Engine.RunTurnAsync("hello");

        Assert.Equal(TurnStatuses.Completed, turn.Status);
        var sent = CallsIn(chat.Requests[1]).Single();
        Assert.DoesNotContain(Secret, sent.Arguments!["extra"]?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task F02_uri_result_must_not_resend_the_known_key_to_a_fake_provider()
    {
        var result = new FunctionResultContent("prior", new Uri("https://example.com/" + Secret));
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent> { new TextContent("answer"), result }))))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Secret });

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);

        var sent = ResultsIn(chat.Requests[1]).Single();
        Assert.DoesNotContain(Secret, sent.Result?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task F02_uri_argument_reaches_the_real_adapter_request_body_with_the_known_key()
    {
        using var server = new FakeChatServer().EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        var call = new FunctionCallContent("uri", "inspect_scene", new Dictionary<string, object?> { ["extra"] = new Uri("https://example.com/" + Secret) });
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent> { call }))))
            .Enqueue((request, ct) => wire.GetResponseAsync(request.Messages, request.Options, ct));
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Secret });

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10))).Status);

        Assert.DoesNotContain(Secret, Assert.Single(server.Requests).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task F03_blank_call_with_an_id_reused_by_a_surviving_call_must_not_disappear()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("same", "", "{}"), ("same", "spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}"))
            .EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);

        var turn = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "", "spawn_objects" }, turn.ToolCalls.Select(c => c.Tool));
        Assert.Equal(new[] { ToolErrorCodes.UnknownTool, ToolErrorCodes.InvalidArguments }, turn.ToolCalls.Select(c => c.ErrorCode));
        Assert.Empty(t.Engine.GetScene().Instances);
    }

    [Fact]
    public async Task F03_reused_id_after_a_blank_call_must_not_mutate_the_scene()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("same", "", "{}"), ("same", "spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}"))
            .EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);

        var turn = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(t.Engine.GetScene().Instances.Count == 0,
            $"Expected no mutation; revision={t.Engine.GetScene().Revision}; trace={string.Join(",", turn.ToolCalls.Select(c => c.Tool + ":" + c.Status))}");
    }

    [Theory]
    [InlineData("SENSEI_LLM_REASONING_EFFORT", "extreme")]
    [InlineData("SENSEI_LLM_MAX_OUTPUT_TOKENS", "not-a-number")]
    public void F04_invalid_S125_environment_values_must_be_rejected(string variable, string value)
    {
        var env = new Dictionary<string, string?> { [LlmSettings.ApiKeyVariable] = Secret, [variable] = value };
        Assert.Throws<LlmConfigurationException>(() => LlmSettings.FromEnvironment(env));
    }

    [Theory]
    [InlineData(null, 2000)]
    [InlineData("321", 321)]
    public async Task F04_every_request_must_carry_the_S125_output_token_cap(string? configured, int expected)
    {
        using var server = new FakeChatServer().EnqueueText("done");
        var env = new Dictionary<string, string?>
        {
            [LlmSettings.ApiKeyVariable] = Secret, [LlmSettings.BaseUrlVariable] = server.BaseUrl,
            [LlmSettings.ModelVariable] = "fake-model", ["SENSEI_LLM_MAX_OUTPUT_TOKENS"] = configured,
        };
        using var wire = ChatClientFactory.Create(LlmSettings.FromEnvironment(env));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10))).Status);

        using var body = JsonDocument.Parse(Assert.Single(server.Requests).Body);
        int? actual = body.RootElement.TryGetProperty("max_completion_tokens", out var completion) ? completion.GetInt32()
            : body.RootElement.TryGetProperty("max_tokens", out var tokens) ? tokens.GetInt32() : null;
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null, "minimal")]
    [InlineData("high", "high")]
    public async Task F04_every_request_must_carry_the_S125_reasoning_effort(string? configured, string expected)
    {
        using var server = new FakeChatServer().EnqueueText("done");
        var env = new Dictionary<string, string?>
        {
            [LlmSettings.ApiKeyVariable] = Secret, [LlmSettings.BaseUrlVariable] = server.BaseUrl,
            [LlmSettings.ModelVariable] = "fake-model", ["SENSEI_LLM_REASONING_EFFORT"] = configured,
        };
        using var wire = ChatClientFactory.Create(LlmSettings.FromEnvironment(env));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10))).Status);

        using var body = JsonDocument.Parse(Assert.Single(server.Requests).Body);
        string? actual = body.RootElement.TryGetProperty("reasoning", out var reasoning) && reasoning.TryGetProperty("effort", out var effort)
            ? effort.GetString() : null;
        Assert.Equal(expected, actual);
    }
}

public sealed class ReviewFinal2Controls
{
    private const string Secret = "review-final2-key-QX42";

    [Fact]
    public async Task Agent_world_player_task_evidence_summary_and_restart_work_together()
    {
        TestEngine? running = null;
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("spawn", "spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"},{\"catalog_id\":\"box\"}]}"))
            .EnqueueFunctionCalls(("task", "start_challenge", """
                {"family":"listen_arrange","instruction":{"english":"Put the cup in the box"},
                 "skill_targets":[{"dimension":"listening","key":"cup","japanese":"コップ"}],
                 "predicates":[{"type":"contained_in","object_id":"cup_1","container_id":"box_1"}]}
                """))
            .Enqueue((_, _) =>
            {
                Assert.True(running!.Engine.PlayerMove("cup_1", null, "box_1").Ok);
                var playerEvent = running.Core.World.Events.Last(e => e.Actor == "player");
                return Task.FromResult(CallsResponse(
                    ("attempt", "record_attempt", "{\"target\":{\"dimension\":\"listening\",\"key\":\"cup\"},\"communicative_success\":true,\"language_assessment\":\"correct\",\"event_ids\":[" + playerEvent.EventId + "]}"),
                    ("note", "remember_note", "{\"key\":\"favorite\",\"value\":\"tea\"}"),
                    ("end", "end_activity", "{\"next_suggestion\":\"Try another object\"}")));
            })
            .EnqueueText("done");
        var store = new InMemoryProfileStore();
        await using (var t = await TestEngine.CreateAsync(chat: chat, store: store))
        {
            running = t;
            var turn = await t.Engine.RunTurnAsync("コップ");
            Assert.Equal(TurnStatuses.Completed, turn.Status);
            Assert.Equal(5, turn.ToolCalls.Count);
            Assert.All(turn.ToolCalls, c => Assert.Equal(ToolStatuses.Success, c.Status));
            Assert.Equal("box_1", t.Engine.GetScene().Instances.Single(i => i.Id == "cup_1").ContainerId);
            Assert.Null(t.Engine.GetActiveTask());
            Assert.Equal("task_completed", Assert.Single(store.Peek("default")!.Evidence).EngineOutcome);
            Assert.Equal("Try another object", t.Engine.GetSessionSummary().NextSuggestion);
        }
        await using var restarted = await TestEngine.CreateAsync(store: store);
        Assert.Empty(restarted.Engine.GetScene().Instances);
        Assert.Null(restarted.Engine.GetActiveTask());
        Assert.Single(restarted.Engine.GetProfile().Evidence);
        Assert.Equal("tea", restarted.Engine.GetProfile().Notes["favorite"]);
    }

    [Fact]
    public async Task Nonblank_id_reuse_is_rejected_without_spawn()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("same", "unknown_tool", "{}"), ("same", "spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}"))
            .EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);

        var turn = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { ToolErrorCodes.UnknownTool, ToolErrorCodes.InvalidArguments }, turn.ToolCalls.Select(c => c.ErrorCode));
        Assert.Empty(t.Engine.GetScene().Instances);
    }

    [Fact]
    public void Plain_clr_sequence_at_and_above_the_limit_is_handled()
    {
        var secrets = new Secrets(new[] { Secret });
        var atLimit = Enumerable.Repeat<object?>(Secret, 9999).ToList();
        var aboveLimit = Enumerable.Repeat<object?>(Secret, 10000).ToList();
        var kept = JsonRedaction.SanitizedValue(atLimit, secrets, out var tooLarge);
        Assert.False(tooLarge);
        Assert.All(Assert.IsType<List<object?>>(kept), x => Assert.Equal("[redacted]", x));
        Assert.Equal("[oversized value]", JsonRedaction.SanitizedValue(aboveLimit, secrets, out tooLarge));
        Assert.True(tooLarge);
    }

    [Fact]
    public async Task String_argument_is_sanitized_on_the_same_real_wire_path()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(("str", "inspect_scene", "{\"extra\":\"https://example.com/" + Secret + "\"}")).EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: wire, knownSecrets: new[] { Secret });

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10))).Status);
        Assert.DoesNotContain(Secret, server.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("[redacted]", server.Requests[1].Body, StringComparison.Ordinal);
    }
}

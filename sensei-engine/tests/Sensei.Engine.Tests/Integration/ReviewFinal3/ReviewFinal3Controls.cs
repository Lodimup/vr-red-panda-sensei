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

namespace Sensei.Engine.Tests.Integration.ReviewFinal3;

public sealed class ReviewFinal3Controls
{
    [Fact]
    public void C01_shallow_element_is_unchanged_and_structured_values_over_the_host_cap_are_rejected()
    {
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
        using var shallow = JsonDocument.Parse(new string('[', 64) + "0" + new string(']', 64), new JsonDocumentOptions { MaxDepth = 65 });
        var ordinary = new List<ChatMessage> { new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("r", shallow.RootElement.Clone()) }) };
        Assert.Same(ordinary, guard.Guarded(ordinary));
        var array = Enumerable.Repeat<object?>(1, 7000).ToList();
        var excessive = new List<ChatMessage> { new(ChatRole.Tool, Enumerable.Range(0, 3).Select(i => (AIContent)new FunctionResultContent("r" + i, array)).ToList()) };
        var error = Assert.Throws<OutboundRejectedException>(() => guard.Guarded(excessive));
        Assert.Equal("too_many_values", error.Reason);
    }

    [Fact]
    public void C02_shallow_json_node_is_kept_or_replaced_at_the_stated_node_boundary()
    {
        var small = JsonNode.Parse("[[0]]")!;
        Assert.IsType<JsonElement>(JsonRedaction.SanitizedValue(small, Secrets.None, out var oversized));
        Assert.False(oversized);
        var large = JsonNode.Parse("[" + string.Join(',', Enumerable.Repeat("0", 10000)) + "]")!;
        var notes = new List<string>();
        var message = new ChatMessage(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("r", large) });
        var result = OutboundMessages.Sanitized(new[] { message }, Secrets.None, notes).Single().Contents.OfType<FunctionResultContent>().Single().Result;
        Assert.Equal("[oversized value]", result);
        Assert.Single(notes);
    }

    [Fact]
    public async Task C03_blank_name_collision_recovery_keeps_order_distinct_ids_and_prevents_mutation()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("same", "", "{}"), ("same", "spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}"))
            .EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "final3-wire-ZX42", "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);
        var turn = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TurnStatuses.Completed, turn.Status);
        Assert.Equal(new[] { "unknown_tool", "invalid_arguments" }, turn.ToolCalls.Select(c => c.ErrorCode));
        Assert.Equal(new[] { "", "spawn_objects" }, turn.ToolCalls.Select(c => c.Tool));
        Assert.Empty(t.Engine.GetScene().Instances);
        var messages = server.Requests[1].Json.GetProperty("messages").EnumerateArray().ToArray();
        var calls = messages.Where(m => m.TryGetProperty("tool_calls", out _)).SelectMany(m => m.GetProperty("tool_calls").EnumerateArray()).Select(c => c.GetProperty("id").GetString()).ToArray();
        var results = messages.Where(m => m.GetProperty("role").GetString() == "tool").Select(m => m.GetProperty("tool_call_id").GetString()).ToArray();
        Assert.Equal(2, calls.Distinct().Count());
        Assert.Equal(calls, results);
    }

    [Fact]
    public async Task C04_cli_real_adapter_console_trace_and_authorization_obey_redaction_contract()
    {
        const string key = "final3+key&ZX42-example";
        using var server = new FakeChatServer().EnqueueToolCalls(("s", "speak", "{\"segments\":[{\"lang\":\"en\",\"text\":\"hello " + key + "\"}]}"))
            .EnqueueText("done");
        var result = await InProcessCli.ScriptAsync(new[] { "hello", "/scene", "/summary", "/quit" }, env: CliRunner.EnvFor(server, key), extra: new[] { "--trace" });
        Assert.Equal(0, result.Code);
        Assert.DoesNotContain(key, result.Text, StringComparison.Ordinal);
        Assert.Contains("Sensei [en]: hello [redacted]", result.Text, StringComparison.Ordinal);
        Assert.Contains("id | catalog | region | slot | in | japanese | reading | english", result.Text, StringComparison.Ordinal);
        var lines = Directory.GetFiles(Path.Combine(result.ProfileDir, "traces"), "*.jsonl").SelectMany(File.ReadAllLines).ToArray();
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.DoesNotContain(key, JsonRedaction.SanitizedText(doc.RootElement, Secrets.None), StringComparison.Ordinal);
        }
        Assert.Equal(2, server.Requests.Count);
        foreach (var request in server.Requests)
        {
            Assert.Equal("Bearer " + key, request.Authorization);
            Assert.DoesNotContain(key, JsonRedaction.SanitizedText(request.Json, Secrets.None), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task C05_gameplay_evidence_summary_and_restart_use_persisted_state()
    {
        TestEngine? live = null;
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("spawn", "spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}"))
            .EnqueueFunctionCalls(("task", "start_challenge", """
                {"family":"listen_arrange","instruction":{"english":"Select the cup"},
                 "skill_targets":[{"dimension":"listening","key":"cup"}],
                 "predicates":[{"type":"selected","object_id":"cup_1"}]}
                """))
            .Enqueue((_, _) =>
            {
                Assert.True(live!.Engine.PlayerSelect("cup_1").Ok);
                var selection = live.Core.World.Events.Last(e => e.Kind == "selected");
                return Task.FromResult(CallsResponse(
                    ("attempt", "record_attempt", "{\"target\":{\"dimension\":\"listening\",\"key\":\"cup\"},\"communicative_success\":true,\"language_assessment\":\"correct\",\"event_ids\":[" + selection.EventId + "]}"),
                    ("note", "remember_note", "{\"key\":\"favorite\",\"value\":\"tea\"}"),
                    ("end", "end_activity", "{\"next_suggestion\":\"Try the box\"}")));
            }).EnqueueText("done");
        var store = new InMemoryProfileStore();
        await using (var t = await TestEngine.CreateAsync(chat: chat, store: store))
        {
            live = t;
            var turn = await t.Engine.RunTurnAsync("the cup");
            Assert.Equal(TurnStatuses.Completed, turn.Status);
            Assert.Equal(5, turn.ToolCalls.Count);
            Assert.All(turn.ToolCalls, c => Assert.Equal(ToolStatuses.Success, c.Status));
            var evidence = Assert.Single(store.Peek("default")!.Evidence);
            Assert.Equal("task_completed", evidence.EngineOutcome);
            Assert.Equal(new[] { "player" }, evidence.Actors);
            Assert.Equal("Try the box", t.Engine.GetSessionSummary().NextSuggestion);
            Assert.Equal("cup_1", t.Engine.GetScene().SelectedId);
        }
        await using var restarted = await TestEngine.CreateAsync(store: store);
        Assert.Empty(restarted.Engine.GetScene().Instances);
        Assert.Null(restarted.Engine.GetActiveTask());
        Assert.Equal("tea", restarted.Engine.GetProfile().Notes["favorite"]);
        Assert.Single(restarted.Engine.GetProfile().Evidence);
    }
}

using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Agent;

/// <summary>s5: what earlier turns leave in the history is kept small; the calls, the learner and the pairing stay (S65, S85).</summary>
public sealed class HistoryCompactionTests
{
    [Fact]
    public async Task Finished_speech_is_compacted_on_the_wire()
    {
        using var server = new FakeChatServer()
            .EnqueueToolCalls(("c1", "speak", "{\"segments\":[{\"lang\":\"ja\",\"text\":\"みず\",\"romaji\":\"mizu\",\"translation\":\"water\"}]}"))
            .EnqueueText("done").EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openrouter", "example-key", null, server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client);
        await t.Engine.RunTurnAsync("first");
        await t.Engine.RunTurnAsync("second");
        var messages = server.Requests.Last().Json.GetProperty("messages").EnumerateArray();
        var call = messages.Single(m => m.TryGetProperty("tool_calls", out _)).GetProperty("tool_calls")[0].GetProperty("function");
        var args = call.GetProperty("arguments").GetString()!;
        Assert.DoesNotContain("\n", args);
        Assert.DoesNotContain("romaji", args);
        Assert.DoesNotContain("translation", args);
        Assert.Contains("みず", args);
    }
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private const string FullResult =
        "{\"call_id\":\"c1\",\"tool\":\"spawn_objects\",\"status\":\"success\",\"error_code\":null,\"detail\":null,\"revision\":4,\"affected_ids\":[\"water_1\",\"tea_1\"],\"data\":{\"big\":\"payload\"}}";

    private static List<ChatMessage> OneTurn(string result = FullResult) => new()
    {
        new ChatMessage(ChatRole.User, "<learner_data>\nlearner_transcript: hi\n</learner_data>"),
        new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new TextContent("private planning nobody hears"),
            new FunctionCallContent("c1", "spawn_objects", new Dictionary<string, object?> { ["objects"] = "x" }),
        }),
        new ChatMessage(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("c1", Json(result)) }),
        new ChatMessage(ChatRole.Assistant, "done"),
    };

    [Theory]
    [InlineData("speak", "{\"segments\":[{\"lang\":\"ja\",\"text\":\"水\",\"reading\":\"みず\",\"romaji\":\"mizu\",\"translation\":\"water\"}]}", "{\"segments\":[{\"lang\":\"ja\",\"text\":\"水\"}]}")]
    [InlineData("show_hint", "{\"content\":{\"japanese\":\"水\",\"english\":\"water\",\"reading\":\"みず\"},\"kind\":\"model_answer\"}", "{\"content\":{\"japanese\":\"水\",\"english\":\"water\"}}")]
    [InlineData("start_challenge", "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"ask\"},\"skill_targets\":[{\"key\":\"water\",\"dimension\":\"vocabulary\"}],\"predicates\":[]}", "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"ask\"},\"skill_targets\":[{\"key\":\"water\"}]}")]
    [InlineData("record_attempt", "{\"target\":{\"key\":\"water\",\"dimension\":\"production\"},\"transcript_excerpt\":\"mizu\",\"communicative_success\":true,\"language_assessment\":\"correct\"}", "{\"target\":{\"key\":\"water\"},\"communicative_success\":true,\"language_assessment\":\"correct\"}")]
    [InlineData("move_object", "{\"object_id\":\"water_1\",\"destination\":{\"region\":\"left\"},\"purpose\":\"arrange\"}", "{\"object_id\":\"water_1\",\"destination\":{\"region\":\"left\"}}")]
    [InlineData("set_support", "{\"reason\":\"learner_request\",\"show_romaji\":true}", "{\"show_romaji\":true}")]
    [InlineData("update_challenge", "{\"action\":\"abandon\",\"reason\":\"done\",\"task_id\":\"t1\"}", "{\"action\":\"abandon\"}")]
    [InlineData("spawn_objects", "{\"objects\":[{\"catalog_id\":\"water\",\"region\":\"center\",\"quantity\":2}]}", "{\"objects\":[{\"catalog_id\":\"water\",\"region\":\"center\"}]}")]
    [InlineData("remove_objects", "{\"object_ids\":[\"water_1\"],\"other\":1}", "{\"object_ids\":[\"water_1\"]}")]
    [InlineData("highlight_objects", "{\"object_ids\":[\"water_1\"],\"duration_ms\":1000}", "{\"object_ids\":[\"water_1\"]}")]
    [InlineData("inspect_scene", "{\"anything\":1}", "{}")]
    public void Finished_call_arguments_keep_only_relevant_fields(string name, string input, string expected)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(input)!;
        var call = new FunctionCallContent("c1", name, args);
        var messages = new List<ChatMessage> { new(ChatRole.Assistant, new List<AIContent> { call }), new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("c1", Json("{\"status\":\"success\"}")) }) };
        var compact = HistoryCompactor.Compact(messages)[0].Contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("c1", compact.CallId);
        Assert.Equal(name, compact.Name);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(expected), JsonSerializer.SerializeToNode(compact.Arguments)));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(input), JsonSerializer.SerializeToNode(args)));
    }

    [Fact]
    public void A_result_keeps_its_status_and_affected_ids_and_drops_the_data()
    {
        var compacted = HistoryCompactor.Compact(OneTurn());
        var result = compacted.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        var json = ResultJson(result);

        Assert.Equal("c1", result.CallId);
        Assert.Equal("success", json.GetProperty("status").GetString());
        Assert.Equal(new[] { "water_1", "tea_1" }, json.GetProperty("affected_ids").EnumerateArray().Select(x => x.GetString()).ToArray());
        foreach (var dropped in new[] { "data", "revision", "call_id", "tool", "error_code", "detail" })
            Assert.False(json.TryGetProperty(dropped, out _), dropped);
    }

    [Fact]
    public void A_failure_keeps_its_error_code_and_detail()
    {
        var compacted = HistoryCompactor.Compact(OneTurn("{\"call_id\":\"c1\",\"tool\":\"move_object\",\"status\":\"failure\",\"error_code\":\"invalid_destination\",\"detail\":\"already there\",\"revision\":2,\"affected_ids\":[],\"data\":null}"));
        var json = ResultJson(compacted.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single());

        Assert.Equal("failure", json.GetProperty("status").GetString());
        Assert.Equal("invalid_destination", json.GetProperty("error_code").GetString());
        Assert.Equal("already there", json.GetProperty("detail").GetString());
        Assert.False(json.TryGetProperty("affected_ids", out _));
    }

    [Fact]
    public void The_calls_the_assistant_text_and_the_learner_stay()
    {
        var compacted = HistoryCompactor.Compact(OneTurn());

        Assert.Contains(compacted, m => m.Contents.OfType<TextContent>().Any(t => t.Text == "done")); // S130: the provider's own words go back, cleaned
        Assert.Equal("spawn_objects", compacted.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single().Name);
        Assert.Contains("learner_transcript: hi", compacted[0].Text);
        Assert.Equal(ChatRole.User, compacted[0].Role);
    }

    [Fact]
    public void Every_call_still_has_exactly_one_result()
    {
        var compacted = HistoryCompactor.Compact(OneTurn());
        var calls = compacted.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.CallId).ToList();
        var results = compacted.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.CallId).ToList();
        Assert.Equal(calls, results);
    }

    [Fact]
    public void Compacting_twice_changes_nothing_more_and_never_touches_the_originals()
    {
        var original = OneTurn();
        var once = HistoryCompactor.Compact(original);
        var twice = HistoryCompactor.Compact(once);

        Assert.Equal(JsonSerializer.Serialize(once.Select(m => m.Contents.Select(c => c.GetType().Name + ":" + (c is FunctionResultContent r ? r.Result?.ToString() : c.ToString())))),
                     JsonSerializer.Serialize(twice.Select(m => m.Contents.Select(c => c.GetType().Name + ":" + (c is FunctionResultContent r ? r.Result?.ToString() : c.ToString())))));
        Assert.Contains("planning", original[1].Contents.OfType<TextContent>().Single().Text);
        Assert.Contains("payload", original[2].Contents.OfType<FunctionResultContent>().Single().Result!.ToString());
    }

    [Fact]
    public void The_synthetic_cancelled_result_survives()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hi"),
            new(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("c9", "speak", new Dictionary<string, object?>()) }),
        };
        HistorySettler.Settle(messages);
        var json = ResultJson(HistoryCompactor.Compact(messages).SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single());
        Assert.Equal("cancelled", json.GetProperty("status").GetString());
        Assert.Equal("cancelled", json.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task A_later_turn_sees_the_compact_result_of_an_earlier_one_on_the_wire()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "a")))
            .EnqueueText("first done")
            .EnqueueText("second done");
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("one");
        await t.Engine.RunTurnAsync("two");

        var last = chat.Requests[^1];
        var result = ResultsIn(last).Single();
        var json = ResultJson(result);
        Assert.Equal("success", StatusOf(result));
        Assert.False(json.TryGetProperty("revision", out _));
        // The first turn's own second request still saw the full result.
        Assert.True(ResultJson(ResultsIn(chat.Requests[1]).Single()).TryGetProperty("revision", out _));
    }

    [Fact]
    public async Task The_scene_in_the_state_leaves_out_what_the_catalog_and_support_lines_already_say()
    {
        var chat = new ScriptedChatClient().EnqueueText("ok");
        await using var t = await EngineAsync(chat);
        t.Engine.BeginTurn("(cli)");
        await t.Engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"apple\"}]}");
        await t.Engine.RunTurnAsync("hi");

        var state = chat.Requests[0].Options!.Instructions!;
        var snapshotLine = state.Split('\n').Single(l => l.StartsWith("- scene snapshot", StringComparison.Ordinal));
        Assert.Contains("\"id\":\"apple_1\"", snapshotLine);
        Assert.Contains("\"region\":\"center\"", snapshotLine);
        Assert.DoesNotContain("\"japanese\"", snapshotLine);
        Assert.DoesNotContain("\"support\"", snapshotLine);
    }
}

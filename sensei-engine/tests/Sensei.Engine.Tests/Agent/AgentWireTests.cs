using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Agent;

/// <summary>
/// The agent loop over the real OpenAI chat-completions client, against a loopback endpoint: what the model really
/// receives on the wire, and what the adapter hands back (raw arguments, usage, HTTP errors).
/// </summary>
public sealed class AgentWireTests
{
    private const string Key = "wire-test-key-1234567890";

    private static IChatClient ClientFor(FakeChatServer server, string model = "wire-model") =>
        ChatClientFactory.Create(new LlmSettings("openai_compatible", Key, model, server.BaseUrl));

    private static async Task<TestEngine> EngineFor(FakeChatServer server, RecordingTraceSink? trace = null, params Sensei.Engine.Tools.SenseiTool[] more) =>
        await TestEngine.CreateAsync(chatClient: ClientFor(server), trace: trace, knownSecrets: new[] { Key }, extraTools: more);

    private static JsonElement Messages(FakeRequest request) => request.Json.GetProperty("messages");

    private static JsonElement ToolMessage(FakeRequest request) => Messages(request).EnumerateArray().Last(m => m.GetProperty("role").GetString() == "tool");

    [Fact]
    public async Task S61_the_wire_request_has_the_model_the_instructions_the_16_tools_and_the_learner_block()
    {
        using var server = new FakeChatServer().EnqueueText("hello");
        await using var t = await EngineFor(server);

        var result = await t.Engine.RunTurnAsync("water please");

        Assert.Equal(TurnStatuses.Completed, result.Status);
        var request = Assert.Single(server.Requests);
        Assert.Equal("Bearer " + Key, request.Authorization);
        var body = request.Json;
        Assert.Equal("wire-model", body.GetProperty("model").GetString());
        Assert.Equal(t.Engine.ToolNames.ToArray(), body.GetProperty("tools").EnumerateArray().Select(x => x.GetProperty("function").GetProperty("name").GetString()!).ToArray());
        var messages = Messages(request).EnumerateArray().ToList();
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("ghost red panda", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[^1].GetProperty("role").GetString());
        Assert.Contains("<learner_data>", messages[^1].GetProperty("content").GetString());
        Assert.Contains("water please", messages[^1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task S61_the_api_key_never_appears_in_any_message_sent_to_the_model()
    {
        using var server = new FakeChatServer().EnqueueText("hello");
        await using var t = await EngineFor(server);
        await t.Engine.RunTurnAsync("my key is " + Key);
        Assert.DoesNotContain(Key, server.Requests[0].Body);
    }

    [Fact]
    public async Task S67_usage_from_the_wire_adds_up_over_the_rounds()
    {
        using var server = new FakeChatServer()
            .EnqueueToolCalls(("call_1", "does_not_exist", "{}"))
            .EnqueueText("done", promptTokens: 30, completionTokens: 4);
        await using var t = await EngineFor(server);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal(2, result.Rounds);
        Assert.Equal(12 + 30, result.InputTokens);
        Assert.Equal(9 + 4, result.OutputTokens);
    }

    [Fact]
    public async Task S85_an_unknown_tool_from_the_wire_is_answered_with_the_engine_result_under_the_provider_call_id()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(("call_abc", "does_not_exist", "{\"x\":1}")).EnqueueText("done");
        await using var t = await EngineFor(server);

        await t.Engine.RunTurnAsync("go");

        Assert.Equal(2, server.Requests.Count);
        var assistant = Messages(server.Requests[1]).EnumerateArray().Single(m => m.GetProperty("role").GetString() == "assistant");
        Assert.Equal("call_abc", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());
        var tool = ToolMessage(server.Requests[1]);
        Assert.Equal("call_abc", tool.GetProperty("tool_call_id").GetString());
        var content = JsonDocument.Parse(tool.GetProperty("content").GetString()!).RootElement;
        Assert.Equal("call_abc", content.GetProperty("call_id").GetString());
        Assert.Equal("failure", content.GetProperty("status").GetString());
        Assert.Equal("unknown_tool", content.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task S85_arguments_that_are_not_json_reach_the_pipeline_as_malformed_json()
    {
        var probe = new ProbeTool();
        var trace = new RecordingTraceSink();
        using var server = new FakeChatServer().EnqueueToolCalls(("call_1", "test_probe", "{\"action\": \"ok\", ")).EnqueueText("done");
        await using var t = await EngineFor(server, trace, probe);

        await t.Engine.RunTurnAsync("go");

        var content = JsonDocument.Parse(ToolMessage(server.Requests[1]).GetProperty("content").GetString()!).RootElement;
        Assert.Equal("invalid_arguments", content.GetProperty("error_code").GetString());
        Assert.Equal("malformed JSON", content.GetProperty("detail").GetString());
        Assert.Equal(0, probe.Executions);
        Assert.Equal("\"[unparseable arguments]\"", Assert.Single(trace.Entries).Arguments);
    }

    [Fact]
    public async Task S83_duplicate_property_names_in_the_raw_arguments_are_refused_through_the_loop()
    {
        var probe = new ProbeTool();
        using var server = new FakeChatServer().EnqueueToolCalls(("call_1", "test_probe", "{\"action\":\"ok\",\"action\":\"fail_unknown\"}")).EnqueueText("done");
        await using var t = await EngineFor(server, null, probe);

        await t.Engine.RunTurnAsync("go");

        var content = JsonDocument.Parse(ToolMessage(server.Requests[1]).GetProperty("content").GetString()!).RootElement;
        Assert.Equal("invalid_arguments", content.GetProperty("error_code").GetString());
        Assert.Contains("duplicate", content.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.Executions);
    }

    [Fact]
    public async Task S21_blank_arguments_from_the_wire_are_malformed_json_and_the_tool_does_not_run()
    {
        var probe = new ProbeTool();
        using var server = new FakeChatServer().EnqueueToolCalls(("call_1", "test_probe", "")).EnqueueText("done");
        await using var t = await EngineFor(server, null, probe);

        await t.Engine.RunTurnAsync("go");

        var content = JsonDocument.Parse(ToolMessage(server.Requests[1]).GetProperty("content").GetString()!).RootElement;
        Assert.Equal("invalid_arguments", content.GetProperty("error_code").GetString());
        Assert.Equal("malformed JSON", content.GetProperty("detail").GetString());
        Assert.Equal(0, probe.Executions);
    }

    [Fact]
    public async Task S85_a_known_tool_from_the_wire_runs_and_its_data_goes_back_as_the_tool_message()
    {
        var probe = new ProbeTool();
        using var server = new FakeChatServer().EnqueueToolCalls(("call_1", "test_probe", "{\"action\":\"ok\",\"text\":\"hi\"}")).EnqueueText("done");
        await using var t = await EngineFor(server, null, probe);

        await t.Engine.RunTurnAsync("go");

        Assert.Equal(1, probe.Executions);
        var content = JsonDocument.Parse(ToolMessage(server.Requests[1]).GetProperty("content").GetString()!).RootElement;
        Assert.Equal("success", content.GetProperty("status").GetString());
        Assert.Equal("hi", content.GetProperty("data").GetProperty("echo").GetString());
    }

    [Theory]
    [InlineData(401, "auth_failed")]
    [InlineData(403, "auth_failed")]
    [InlineData(402, "quota_exceeded")]
    public async Task S67_http_errors_from_the_wire_map_to_turn_error_codes(int status, string expected)
    {
        using var server = new FakeChatServer().EnqueueStatus(status);
        await using var t = await EngineFor(server);

        var result = await t.Engine.RunTurnAsync("go");

        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal(expected, result.ErrorCode);
        Assert.DoesNotContain(Key, result.ErrorDetail);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task S67_a_closed_port_is_a_network_error()
    {
        string baseUrl;
        using (var server = new FakeChatServer()) baseUrl = server.BaseUrl;
        var settings = new LlmSettings("openai_compatible", Key, "m", baseUrl);
        await using var t = await TestEngine.CreateAsync(chatClient: ChatClientFactory.Create(settings));

        var result = await t.Engine.RunTurnAsync("go").WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal("network_error", result.ErrorCode);
    }

    // ---------------------------------------------------------------- unit: argument text

    [Fact]
    public void S83_the_raw_arguments_the_provider_kept_win_over_the_parsed_dictionary()
    {
        var call = new FunctionCallContent("c1", "test_probe", new Dictionary<string, object?> { ["action"] = "ok" })
        {
            RawRepresentation = ChatToolCall.CreateFunctionToolCall("c1", "test_probe", BinaryData.FromString("{\"action\":\"a\",\"action\":\"b\"}")),
        };
        Assert.Equal("{\"action\":\"a\",\"action\":\"b\"}", EngineFunctionInvoker.ArgumentsText(call));
    }

    [Fact]
    public void S85_arguments_are_rebuilt_from_the_dictionary_when_there_is_no_raw_text()
    {
        var call = new FunctionCallContent("c1", "test_probe", ScriptedChatClient.ParseArguments("{\"action\":\"ok\",\"count\":3,\"flag\":true,\"ids\":[\"a\"]}"));
        using var doc = JsonDocument.Parse(EngineFunctionInvoker.ArgumentsText(call));
        Assert.Equal("ok", doc.RootElement.GetProperty("action").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("flag").GetBoolean());
        Assert.Equal("a", doc.RootElement.GetProperty("ids")[0].GetString());
    }

    [Fact]
    public void S85_no_arguments_at_all_are_an_empty_object()
    {
        var call = new FunctionCallContent("c1", "inspect_scene", null);
        Assert.Equal("{}", EngineFunctionInvoker.ArgumentsText(call));
    }

    [Fact]
    public void S85_a_parse_failure_without_raw_text_is_not_valid_json()
    {
        var call = new FunctionCallContent("c1", "inspect_scene", null) { Exception = new InvalidOperationException("bad") };
        var text = EngineFunctionInvoker.ArgumentsText(call);
        Assert.Equal(EngineFunctionInvoker.UnparsedArguments, text);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(text));
    }
}

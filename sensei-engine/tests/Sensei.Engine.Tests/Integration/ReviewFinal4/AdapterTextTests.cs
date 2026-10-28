using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Trace;
using static Sensei.Engine.Tests.Integration.ReviewFinal4.ReviewFinal4Tests;

namespace Sensei.Engine.Tests.Integration.ReviewFinal4;

/// <summary>
/// S133, the real-adapter path: every function result reaches the SDK adapter as the JSON text the engine's bounded iterative writer wrote,
/// so the adapter never serializes an element, node or collection itself and its own depth limit cannot end a request outside the
/// boundary. For a result of ordinary depth the wire is byte-identical to what the adapter wrote before: each case below asks the same
/// adapter, directly, what it writes for the value the engine used to hand it, and compares the whole tool message. Invented values,
/// loopback server only.
/// </summary>
public sealed class AdapterTextTests
{
    private static JsonElement Element(string raw)
    {
        using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 20_001 });
        return doc.RootElement.Clone();
    }

    private static object? Shallow(string kind) => kind switch
    {
        "object" => Element("{\"a\":1,\"b\":[true,null,\"x\\u00e9\\\"y\"],\"c\":{\"d\":[]},\"e\":{}}"),
        "numbers" => Element("[1.50,1e3,-0,0.1,12345678901234567890,2E-2]"),
        "unicode" => Element("{\"k\":\"\\u65e5\\u672c\\u8a9e \\u0041 \\n tab\\t <b>&'\\\"\"}"),
        "duplicate_names" => Element("{\"a\":1,\"a\":2}"),
        "element_string" => Element("\"hello\""),
        "element_number" => Element("5"),
        "element_true" => Element("true"),
        "element_null" => Element("null"),
        "empty_array" => Element("[]"),
        "dictionary" => new Dictionary<string, object?> { ["a"] = 1, ["b"] = null, ["c"] = new List<object?> { "x", true, Element("{\"z\":0}") } },
        "list" => new List<object?> { 1, "two", null, Element("{\"z\":0}"), new Dictionary<string, object?> { ["k"] = "v" } },
        "record" => new RepairBoundaryTests.Holder("note", 3),
        "plain_string" => "hello",
        "json_text_string" => "{\"a\":1}",
        "boolean" => true,
        "integer" => 5,
        "double" => 1.5,
        "null" => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ToolMessage(FakeRequest request) => request.Json.GetProperty("messages").EnumerateArray()
        .Single(m => m.GetProperty("role").GetString() == "tool").GetRawText();

    private static string NormalizeMessage(string message)
    {
        using var outer = JsonDocument.Parse(message);
        var content = outer.RootElement.GetProperty("content").GetString()!;
        try { using var inner = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 20_001 }); return JsonSerializer.Serialize(inner.RootElement); }
        catch (JsonException) { return content; }
    }

    private static string ToolContent(FakeRequest request) => request.Json.GetProperty("messages").EnumerateArray()
        .Single(m => m.GetProperty("role").GetString() == "tool").GetProperty("content").GetString()!;

    /// <summary>The tool message the real adapter writes when it is handed <paramref name="handed"/> as a result, or null when it cannot write it.</summary>
    private static async Task<string?> AdapterWrites(object? handed)
    {
        using var server = new FakeChatServer().EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "adapter-text-example", "fake-model", server.BaseUrl));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello"), new(ChatRole.Assistant, "answer"),
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("prior", handed) }),
        };
        try { await wire.GetResponseAsync(messages); }
        catch (Exception) { return null; }
        return server.Requests.Count == 1 ? ToolMessage(server.Requests[0]) : null;
    }

    /// <summary>The tool message the engine's own client writes in the next turn when the model's previous result was <paramref name="value"/>.</summary>
    private static async Task<(TurnResult Turn, FakeChatServer Server)> EngineWrites(object? value)
    {
        var server = new FakeChatServer().EnqueueText("done");
        var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "adapter-text-example", "fake-model", server.BaseUrl));
        var chat = new SeededWireClient(wire, value!);
        var t = await TestEngine.CreateAsync(chatClient: chat);
        try
        {
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
            return (await t.Engine.RunTurnAsync("two"), server);
        }
        finally
        {
            await t.DisposeAsync();
            chat.Dispose();
        }
    }

    [Theory]
    [InlineData("object")]
    [InlineData("numbers")]
    [InlineData("unicode")]
    [InlineData("duplicate_names")]
    [InlineData("element_string")]
    [InlineData("element_number")]
    [InlineData("element_true")]
    [InlineData("element_null")]
    [InlineData("empty_array")]
    [InlineData("dictionary")]
    [InlineData("list")]
    [InlineData("record")]
    [InlineData("plain_string")]
    [InlineData("json_text_string")]
    [InlineData("boolean")]
    [InlineData("integer")]
    [InlineData("double")]
    [InlineData("null")]
    public async Task A_result_of_ordinary_depth_is_written_to_the_wire_exactly_as_the_adapter_wrote_it_before(string kind)
    {
        var value = Shallow(kind);
        // What the engine handed the adapter before this change: the sanitized value itself.
        var before = await AdapterWrites(JsonRedaction.SanitizedValue(value, Secrets.None));
        Assert.NotNull(before);

        var (turn, server) = await EngineWrites(value);
        using var _ = server;

        Assert.Equal(TurnStatuses.Completed, turn.Status);
        Assert.Equal(NormalizeMessage(before!), NormalizeMessage(ToolMessage(Assert.Single(server.Requests))));
    }

    [Fact]
    public async Task One_object_result_is_the_adapters_indented_text_not_a_quoted_copy_of_it()
    {
        var (turn, server) = await EngineWrites(Element("{\"a\":1,\"b\":[2]}"));
        using var _ = server;

        Assert.Equal(TurnStatuses.Completed, turn.Status);
        Assert.Equal("{\"a\":1,\"b\":[2]}", ToolContent(Assert.Single(server.Requests)));
    }

    [Theory]
    [InlineData(62)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(66)]
    public async Task At_the_adapters_own_depth_limit_the_text_is_the_adapters_where_it_wrote_one_and_compact_where_it_could_not(int depth)
    {
        var raw = new string('[', depth) + "0" + new string(']', depth);
        using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = depth + 1 });
        var element = doc.RootElement.Clone();
        var before = await AdapterWrites(element);

        var (turn, server) = await EngineWrites(element);
        using var _ = server;

        Assert.Equal(TurnStatuses.Completed, turn.Status);
        var request = Assert.Single(server.Requests);
        if (before is not null) Assert.Equal(NormalizeMessage(before), NormalizeMessage(ToolMessage(request)));
        else Assert.Equal(raw, ToolContent(request));
    }

    [Theory]
    [InlineData(70)]
    [InlineData(200)]
    [InlineData(1_500)]
    [InlineData(9_000)]
    public async Task A_deep_element_result_completes_with_exactly_one_request_and_carries_the_whole_value(int depth)
    {
        var raw = new string('[', depth) + "0" + new string(']', depth);
        using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = depth + 1 });

        var (turn, server) = await EngineWrites(doc.RootElement.Clone());
        using var _ = server;

        Assert.Equal(TurnStatuses.Completed, turn.Status);
        var request = Assert.Single(server.Requests);
        Assert.Equal(raw, ToolContent(request));
    }

    [Fact]
    public async Task A_result_over_the_value_cap_is_still_replaced_whole_and_the_request_goes()
    {
        var raw = new string('[', 10_001) + "0" + new string(']', 10_001);
        using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 10_002 });

        var (turn, server) = await EngineWrites(doc.RootElement.Clone());
        using var _ = server;

        Assert.Equal(TurnStatuses.Completed, turn.Status);
        Assert.Equal("[oversized value]", ToolContent(Assert.Single(server.Requests)));
    }

    [Fact]
    public async Task A_secret_inside_a_deep_or_shallow_result_is_cleaned_before_it_becomes_text()
    {
        const string key = "adapter-text-secret-ZX42";
        var deep = new string('[', 200) + "\"" + key + "\"" + new string(']', 200);
        foreach (var raw in new[] { "{\"" + key + "\":\"" + key + "\"}", deep })
        {
            using var server = new FakeChatServer().EnqueueText("done");
            using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", key, "fake-model", server.BaseUrl));
            using var chat = new SeededWireClient(wire, Element(raw));
            await using var t = await TestEngine.CreateAsync(chatClient: chat);
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);

            var request = Assert.Single(server.Requests);
            Assert.DoesNotContain(key, request.Body, StringComparison.Ordinal);
            Assert.Contains("[redacted]", ToolContent(request), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_host_client_still_receives_the_result_as_the_json_element_it_was()
    {
        var element = Element("{\"a\":[1,2]}");
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(ResultMessage(element))))
            .EnqueueText("done");
        await using var t = await TestEngine.CreateAsync(chat: chat);

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);

        var handed = chat.Requests[1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single(r => r.CallId == "prior").Result;
        Assert.IsType<JsonElement>(handed);
        Assert.Equal("{\"a\":[1,2]}", ((JsonElement)handed!).GetRawText());
    }

    [Fact]
    public void The_sanitizer_hands_text_only_when_asked_and_leaves_text_null_and_scalars_alone()
    {
        var message = new ChatMessage(ChatRole.Tool, new List<AIContent>
        {
            new FunctionResultContent("a", Element("{\"k\":1}")), new FunctionResultContent("b", new List<object?> { 1, "two" }),
            new FunctionResultContent("c", "plain"), new FunctionResultContent("d", null), new FunctionResultContent("e", 7),
            new FunctionResultContent("f", true),
        });

        object?[] Results(bool asText) => OutboundMessages.Sanitized(new[] { message }, Secrets.None, new List<string>(), asText)
            .Single().Contents.OfType<FunctionResultContent>().Select(r => r.Result).ToArray();

        var asIs = Results(false);
        Assert.IsType<JsonElement>(asIs[0]);
        Assert.IsType<List<object?>>(asIs[1]);

        var asText = Results(true);
        Assert.Equal("{\"k\":1}", asText[0]);
        Assert.Equal("[1,\"two\"]", asText[1]);
        Assert.Equal("plain", asText[2]);
        Assert.Null(asText[3]);
        Assert.Equal(7, asText[4]);
        Assert.Equal(true, asText[5]);
    }
}

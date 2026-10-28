using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sensei.Engine.Tests.Integration.ReviewFinal2;

/// <summary>Helpers shared by the D6 tests: every string value and property name of a JSON text, after unescaping.</summary>
internal static class D6Json
{
    /// <summary>Every string value and every property name in <paramref name="json"/>, as the parser unescapes them (iterative).</summary>
    public static List<string> Texts(string json)
    {
        var found = new List<string>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { MaxDepth = 100_000 });
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName) found.Add(reader.GetString()!);
        }
        return found;
    }

    /// <summary>The key must be in none of the unescaped strings or names, and not in the raw text either.</summary>
    public static void AssertAbsent(string json, params string[] keys)
    {
        foreach (var key in keys)
        {
            Assert.DoesNotContain(key, json, StringComparison.Ordinal);
            foreach (var text in Texts(json)) Assert.DoesNotContain(key, text, StringComparison.Ordinal);
        }
    }

    /// <summary>The JSON with every occurrence of <paramref name="key"/> inside a string value or property name replaced, structure untouched.</summary>
    public static string Replaced(string json, string key)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 1000 })!;
        return Replace(root, key)!.ToJsonString();
    }

    private static JsonNode? Replace(JsonNode? node, string key)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var copy = new JsonObject();
                foreach (var pair in obj) copy[pair.Key.Replace(key, "[redacted]", StringComparison.Ordinal)] = Replace(pair.Value, key);
                return copy;
            }
            case JsonArray array:
            {
                var copy = new JsonArray();
                foreach (var item in array) copy.Add(Replace(item, key));
                return copy;
            }
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(text.Replace(key, "[redacted]", StringComparison.Ordinal));
            default:
                return node?.DeepClone();
        }
    }
}

/// <summary>A tool declaration whose parameter schema has one property with a chosen name: a real property name inside a request body.</summary>
internal sealed class NamedPropertyFunction : AIFunction
{
    private readonly JsonElement _schema;

    public NamedPropertyFunction(string property)
    {
        using var doc = JsonDocument.Parse(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { [property] = new JsonObject { ["type"] = "string" } },
        }.ToJsonString());
        _schema = doc.RootElement.Clone();
    }

    public override string Name => "lookup";

    public override string Description => "Looks something up.";

    public override JsonElement JsonSchema => _schema;

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("declaration only");
}

/// <summary>D6: the provider request is sanitized in the bytes that leave, whatever shape the value had in memory.</summary>
public sealed class OutboundWireGuardTests
{
    private const string Key = "d6-key-QX42-example";

    /// <summary>A key made of characters that are not letters or digits but are common in real keys.</summary>
    private const string PlusKey = "d6+key/QX42=example";

    /// <summary>A key with a quote and a backslash, which the request body writer spells with escapes.</summary>
    private const string EscKey = "d6-key-\"q\\-QX42-ex";

    private static IChatClient Wire(FakeChatServer server, string key) =>
        ChatClientFactory.Create(new LlmSettings("openai_compatible", key, "fake-model", server.BaseUrl));

    private static List<ChatMessage> Conversation(object? argument, object? result)
    {
        var call = new FunctionCallContent("c1", "inspect_scene", new Dictionary<string, object?> { ["extra"] = argument });
        return new List<ChatMessage>
        {
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, new List<AIContent> { call }),
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("c1", result) }),
        };
    }

    private static async Task<(string Body, string? Authorization)> SendAsync(string key, List<ChatMessage> messages, ChatOptions? options = null)
    {
        using var server = new FakeChatServer().EnqueueText("done");
        using var wire = Wire(server, key);
        await wire.GetResponseAsync(messages, options).WaitAsync(TimeSpan.FromSeconds(10));
        var request = Assert.Single(server.Requests);
        return (request.Body, request.Authorization);
    }

    public static TheoryData<string, string> Shapes()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in new[] { Key, PlusKey })
        {
            data.Add("uri_in_arguments", key);
            data.Add("uri_in_result", key);
            data.Add("uri_in_a_list_in_arguments", key);
            data.Add("string_with_the_key", key);
            data.Add("json_in_a_string_argument", key);
            data.Add("json_in_a_string_result", key);
            data.Add("key_pair_dictionary", key);
            data.Add("text_content", key);
            data.Add("property_name_in_a_tool_schema", key);
        }
        // A key with a quote and a backslash is spelled with escapes in the body; inside a string that holds JSON, the JSON writer's own
        // escapes sit in front of it, so only the shapes that put it straight into the body are listed for it.
        data.Add("text_content", EscKey);
        data.Add("property_name_in_a_tool_schema", EscKey);
        return data;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task D6_the_key_never_reaches_the_request_body_whatever_shape_held_it(string shape, string key)
    {
        var uri = new Uri("https://example.com/" + key);
        ChatOptions? options = null;
        List<ChatMessage> messages;
        switch (shape)
        {
            case "uri_in_arguments":
                messages = Conversation(uri, "ok");
                break;
            case "uri_in_result":
                messages = Conversation("fine", uri);
                break;
            case "uri_in_a_list_in_arguments":
                messages = Conversation(new List<object?> { "a", new List<object?> { uri } }, "ok");
                break;
            case "string_with_the_key":
                messages = Conversation("note " + key + " end", "result " + key);
                break;
            case "json_in_a_string_argument":
                messages = Conversation("{\"note\":\"" + key + "\",\"more\":[1,2]}", "ok");
                break;
            case "json_in_a_string_result":
                messages = Conversation("fine", "{\"detail\":{\"token\":\"" + key + "\"}}");
                break;
            case "key_pair_dictionary":
                messages = Conversation(new Dictionary<string, string> { ["k"] = key }, new Dictionary<int, string> { [1] = key });
                break;
            case "text_content":
                messages = Conversation("a", "b");
                messages[0] = new ChatMessage(ChatRole.User, "my key is " + key);
                break;
            case "property_name_in_a_tool_schema":
                messages = Conversation("a", "b");
                options = new ChatOptions { Tools = new List<AITool> { new NamedPropertyFunction(key) } };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        var (body, authorization) = await SendAsync(key, messages, options);

        D6Json.AssertAbsent(body, key);
        Assert.Contains("[redacted]", body, StringComparison.Ordinal);
        Assert.Equal("Bearer " + key, authorization);
    }

    /// <summary>The same client without the guard in its pipeline: what the body would hold.</summary>
    private static IChatClient Unguarded(FakeChatServer server, string key) =>
        new RequestOptionsChatClient(
            new ChatClient("fake-model", new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri(server.BaseUrl) }).AsIChatClient(),
            new LlmSettings("openai_compatible", key, "fake-model", server.BaseUrl), new OutboundGuard(Secrets.None));

    [Fact]
    public async Task D6_a_key_the_body_writer_spells_with_escapes_is_found_after_unescaping()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "x " + EscKey + " y") };
        var spelled = EscKey.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        using var plainServer = new FakeChatServer().EnqueueText("done");
        using var plain = Unguarded(plainServer, EscKey);
        await plain.GetResponseAsync(messages).WaitAsync(TimeSpan.FromSeconds(10));
        var plainBody = Assert.Single(plainServer.Requests).Body;
        // The body writer spells the quote and the backslash with escapes: a search of the raw bytes for the key finds nothing, yet the key is in the request.
        Assert.DoesNotContain(EscKey, plainBody, StringComparison.Ordinal);
        Assert.Contains(spelled, plainBody, StringComparison.Ordinal);
        Assert.Contains(D6Json.Texts(plainBody), text => text.Contains(EscKey, StringComparison.Ordinal));

        var (body, _) = await SendAsync(EscKey, messages);
        D6Json.AssertAbsent(body, EscKey);
        Assert.DoesNotContain(spelled, body, StringComparison.Ordinal);
        Assert.Equal("x [redacted] y", JsonDocument.Parse(body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task D6_a_credential_pattern_match_is_removed_too()
    {
        var token = "sk-" + new string('a', 20);
        var (body, _) = await SendAsync(Key, Conversation(new Uri("https://example.com/" + token), "password: hunter2hunter2"));

        D6Json.AssertAbsent(body, token, "hunter2hunter2");
    }

    [Fact]
    public async Task D6_an_ordinary_request_is_sent_byte_for_byte()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "日本語 + & < > ' \" \\ / emoji \U0001F43C tab\tnewline\n"),
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("c1", "inspect_scene", new Dictionary<string, object?>
                {
                    ["n"] = 1.50, ["big"] = 12345678901234L, ["flag"] = true, ["none"] = null,
                    ["nested"] = new Dictionary<string, object?> { ["list"] = new List<object?> { 1, "two", false } },
                    ["raw"] = JsonDocument.Parse("{\"e\":1E+3,\"z\":-0.0}").RootElement.Clone(),
                }),
            }),
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("c1", JsonDocument.Parse("{\"status\":\"success\",\"data\":{\"instances\":[]}}").RootElement.Clone()) }),
        };
        var options = new ChatOptions { Tools = new List<AITool> { new NamedPropertyFunction("object_id") }, Instructions = "Be kind." };

        using var guardedServer = new FakeChatServer().EnqueueText("done");
        using var plainServer = new FakeChatServer().EnqueueText("done");
        var settingsGuarded = new LlmSettings("openai_compatible", Key, "fake-model", guardedServer.BaseUrl);
        var settingsPlain = new LlmSettings("openai_compatible", Key, "fake-model", plainServer.BaseUrl);
        using var guarded = ChatClientFactory.Create(settingsGuarded);
        using var plain = new RequestOptionsChatClient(
            new ChatClient("fake-model", new ApiKeyCredential(Key), new OpenAIClientOptions { Endpoint = new Uri(plainServer.BaseUrl) }).AsIChatClient(),
            settingsPlain, new OutboundGuard(Secrets.None));

        await guarded.GetResponseAsync(messages, options).WaitAsync(TimeSpan.FromSeconds(10));
        await plain.GetResponseAsync(messages, options).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(Assert.Single(plainServer.Requests).Body, Assert.Single(guardedServer.Requests).Body);
    }

    [Fact]
    public async Task D6_a_cleaned_request_differs_from_the_plain_one_only_in_the_strings_that_held_the_key()
    {
        var messages = Conversation(new Uri("https://example.com/" + Key), new Dictionary<string, string> { ["k"] = Key });
        messages.Insert(0, new ChatMessage(ChatRole.User, "日本語 + & " + Key));
        var options = new ChatOptions { Tools = new List<AITool> { new NamedPropertyFunction("object_id") } };

        using var guardedServer = new FakeChatServer().EnqueueText("done");
        using var plainServer = new FakeChatServer().EnqueueText("done");
        using var guarded = ChatClientFactory.Create(new LlmSettings("openai_compatible", Key, "fake-model", guardedServer.BaseUrl));
        var settingsPlain = new LlmSettings("openai_compatible", Key, "fake-model", plainServer.BaseUrl);
        using var plain = new RequestOptionsChatClient(
            new ChatClient("fake-model", new ApiKeyCredential(Key), new OpenAIClientOptions { Endpoint = new Uri(plainServer.BaseUrl) }).AsIChatClient(),
            settingsPlain, new OutboundGuard(Secrets.None));

        await guarded.GetResponseAsync(messages, options).WaitAsync(TimeSpan.FromSeconds(10));
        await plain.GetResponseAsync(messages, options).WaitAsync(TimeSpan.FromSeconds(10));

        var plainBody = Assert.Single(plainServer.Requests).Body;
        Assert.Contains(Key, plainBody, StringComparison.Ordinal);
        var guardedBody = Assert.Single(guardedServer.Requests).Body;
        D6Json.AssertAbsent(guardedBody, Key);
        Assert.Equal(JsonNode.Parse(D6Json.Replaced(plainBody, Key))!.ToJsonString(), JsonNode.Parse(guardedBody)!.ToJsonString());
    }

    [Fact]
    public async Task D6_a_retried_request_is_sent_again_with_the_same_cleaned_body()
    {
        using var server = new FakeChatServer().EnqueueStatus(500).EnqueueText("done");
        using var wire = Wire(server, Key);

        var response = await wire.GetResponseAsync(Conversation(new Uri("https://example.com/" + Key), "ok")).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("done", response.Text);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(server.Requests[0].Body, server.Requests[1].Body);
        D6Json.AssertAbsent(server.Requests[1].Body, Key);
    }

    [Fact]
    public async Task D6_the_guard_belongs_to_the_client_and_is_found_through_a_wrapper_that_forwards_services()
    {
        using var server = new FakeChatServer();
        using var wire = Wire(server, Key);
        using var wrapped = new ForwardingClient(wire);

        Assert.IsType<OutboundGuard>(wire.GetService(typeof(OutboundGuard)));
        Assert.Same(wire.GetService(typeof(OutboundGuard)), wrapped.GetService(typeof(OutboundGuard)));
        await Task.CompletedTask;
    }
}

/// <summary>D6: a body the guard cannot walk is never sent, and the pipeline does not retry it.</summary>
public sealed class OutboundGuardRejectionTests
{
    private const string Key = "d6-key-QX42-example";

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    private sealed class CountingPolicy : PipelinePolicy
    {
        public int Passes;

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            Interlocked.Increment(ref Passes);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            Interlocked.Increment(ref Passes);
            await ProcessNextAsync(message, pipeline, currentIndex);
        }
    }

    private static (ClientPipeline Pipeline, CountingHandler Handler, CountingPolicy Counter) Pipeline()
    {
        var handler = new CountingHandler();
        var counter = new CountingPolicy();
        var options = new ClientPipelineOptions { Transport = new HttpClientPipelineTransport(new HttpClient(handler)) };
        var pipeline = ClientPipeline.Create(options, new PipelinePolicy[0], new PipelinePolicy[0], new PipelinePolicy[] { counter, new OutboundGuard(new Secrets(new[] { Key })) });
        return (pipeline, handler, counter);
    }

    private static PipelineMessage Message(ClientPipeline pipeline, byte[] body)
    {
        var message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri("http://127.0.0.1:9/v1/chat/completions");
        message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(body));
        return message;
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Ones(int values) => "[" + string.Join(",", Enumerable.Repeat("1", values - 1)) + "]";

    public static TheoryData<string, string> Bad()
    {
        var data = new TheoryData<string, string>
        {
            { "not json", OutboundRejectedException.NotJson },
            { "{\"a\":1} {\"b\":2}", OutboundRejectedException.NotJson },
            { "{\"a\":1,}", OutboundRejectedException.NotJson },
            { "{\"a\":1 /* c */}", OutboundRejectedException.NotJson },
            { "{\"a\":\"\\ud800\"}", OutboundRejectedException.NotJson },
            { "", OutboundRejectedException.NotJson },
            { "   ", OutboundRejectedException.NotJson },
            { Ones(20_001), OutboundRejectedException.TooManyValues },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Bad))]
    public void D6_a_body_that_cannot_be_walked_is_not_sent_and_not_retried(string body, string reason)
    {
        var (pipeline, handler, counter) = Pipeline();
        var error = Assert.Throws<OutboundRejectedException>(() => pipeline.Send(Message(pipeline, Bytes(body))));

        Assert.Equal(reason, error.Reason);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, counter.Passes);
    }

    [Theory]
    [MemberData(nameof(Bad))]
    public async Task D6_the_async_path_rejects_the_same_bodies(string body, string reason)
    {
        var (pipeline, handler, counter) = Pipeline();
        var error = await Assert.ThrowsAsync<OutboundRejectedException>(async () => await pipeline.SendAsync(Message(pipeline, Bytes(body))));

        Assert.Equal(reason, error.Reason);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, counter.Passes);
    }

    [Fact]
    public void D6_invalid_utf8_is_not_sent()
    {
        var (pipeline, handler, _) = Pipeline();
        var error = Assert.Throws<OutboundRejectedException>(() => pipeline.Send(Message(pipeline, new byte[] { (byte)'{', (byte)'"', 0xC3, 0x28, (byte)'"', (byte)':', (byte)'1', (byte)'}' })));

        Assert.Equal(OutboundRejectedException.NotJson, error.Reason);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void D6_a_body_at_the_value_cap_is_sent()
    {
        var (pipeline, handler, _) = Pipeline();
        pipeline.Send(Message(pipeline, Bytes(Ones(20_000))));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void D6_a_very_deep_body_within_the_cap_is_walked_without_recursion()
    {
        var depth = 15_000;
        var body = new string('[', depth) + "\"" + Key + "\"" + new string(']', depth);
        var (pipeline, handler, _) = Pipeline();
        var message = Message(pipeline, Bytes(body));

        pipeline.Send(message);

        Assert.Equal(1, handler.Calls);
        var sent = new MemoryStream();
        message.Request.Content!.WriteTo(sent);
        var text = Encoding.UTF8.GetString(sent.ToArray());
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void D6_a_request_without_a_body_passes_untouched()
    {
        var (pipeline, handler, _) = Pipeline();
        var message = pipeline.CreateMessage();
        message.Request.Method = "GET";
        message.Request.Uri = new Uri("http://127.0.0.1:9/v1/models");

        pipeline.Send(message);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void D6_a_string_that_holds_json_is_only_a_string()
    {
        var inner = "{\"a\":\"" + Key + "\"}";
        var body = "{\"arguments\":" + JsonSerializer.Serialize(inner) + "}";
        var (pipeline, _, _) = Pipeline();
        var message = Message(pipeline, Bytes(body));

        pipeline.Send(message);

        var sent = new MemoryStream();
        message.Request.Content!.WriteTo(sent);
        var arguments = JsonDocument.Parse(sent.ToArray()).RootElement.GetProperty("arguments").GetString();
        Assert.Equal("{\"a\":\"[redacted]\"}", arguments);
    }

    [Fact]
    public async Task D6_through_the_engine_a_request_the_guard_rejects_fails_the_turn_with_provider_error_and_traces_it()
    {
        using var server = new FakeChatServer().EnqueueText("never sent");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", Key, "fake-model", server.BaseUrl));
        using var oversized = new BigBodyClient(wire);
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(chatClient: oversized, trace: trace, knownSecrets: new[] { Key });

        var turn = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(TurnStatuses.Failed, turn.Status);
        Assert.Equal(TurnErrorCodes.ProviderError, turn.ErrorCode);
        Assert.Equal("outbound request rejected: too_many_values", turn.ErrorDetail);
        Assert.Empty(server.Requests);
        var line = Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
        Assert.Equal("engine:outbound_rejected", line.Tool);
        Assert.Equal("request not sent: too_many_values", line.Arguments);
        Assert.Equal(ToolStatuses.Failure, line.Status);
    }

    /// <summary>A host wrapper that makes the request body bigger than the guard walks (a JSON patch adds an array of 20,001 values).</summary>
    private sealed class BigBodyClient : DelegatingChatClient
    {
        public BigBodyClient(IChatClient inner) : base(inner)
        {
        }

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var changed = options?.Clone() ?? new ChatOptions();
            var previous = changed.RawRepresentationFactory;
            changed.RawRepresentationFactory = client =>
            {
                var raw = previous?.Invoke(client) as ChatCompletionOptions ?? new ChatCompletionOptions();
#pragma warning disable SCME0001
                raw.Patch.Set("$.extra"u8, BinaryData.FromString(Ones(20_001)));
#pragma warning restore SCME0001
                return raw;
            };
            return base.GetResponseAsync(messages, changed, cancellationToken);
        }
    }
}

/// <summary>A wrapper a host might stack on a client; it forwards everything, services included.</summary>
internal sealed class ForwardingClient : DelegatingChatClient
{
    public ForwardingClient(IChatClient inner) : base(inner)
    {
    }
}

/// <summary>D6: a real request of the engine is nowhere near the value cap, and the guard leaves it alone.</summary>
public sealed class OutboundRequestSizeTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public OutboundRequestSizeTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task D6_the_largest_ordinary_request_has_far_fewer_values_than_the_cap_and_needs_no_cleaning()
    {
        using var server = new FakeChatServer();
        const int Turns = 14;
        for (var i = 0; i < Turns; i++)
        {
            server.EnqueueToolCalls(("spawn" + i, "spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}"), ("look" + i, "inspect_scene", "{}"));
            server.EnqueueText("done " + i);
        }
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "size-key-QX42-example", "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);

        for (var i = 0; i < Turns; i++)
        {
            var turn = await t.Engine.RunTurnAsync("turn " + i).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(TurnStatuses.Completed, turn.Status);
            if (t.Engine.GetScene().Instances.Count >= 14) break;
        }

        var largest = 0;
        foreach (var request in server.Requests)
        {
            var status = JsonRedaction.SanitizeJson(System.Text.Encoding.UTF8.GetBytes(request.Body), text => text, int.MaxValue, out _, out var values, out var changed);
            Assert.Equal(JsonWalkStatus.Ok, status);
            Assert.False(changed);
            largest = Math.Max(largest, values);
        }
        _output.WriteLine("requests=" + server.Requests.Count + " largest_values=" + largest);
        Assert.InRange(largest, 1, JsonRedaction.MaxFinalValues / 4);
    }
}

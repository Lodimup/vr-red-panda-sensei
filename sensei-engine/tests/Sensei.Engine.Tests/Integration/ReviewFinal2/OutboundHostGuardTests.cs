using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sensei.Engine.Tests.Integration.ReviewFinal2;

/// <summary>A value of a type the engine knows nothing about, whose public text holds the key.</summary>
internal sealed record Credentialed(string Name, string Secret);

/// <summary>A value no serializer can write.</summary>
internal sealed class Unwritable
{
    public string Text => throw new InvalidOperationException("this value cannot be written");
}

/// <summary>D6: a chat client the engine did not build gets the same final check on the JSON form of what it is handed.</summary>
public sealed class OutboundHostGuardTests
{
    private const string Key = "d6-host-key-QX42-example";

    private static string Json(object? value) => JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions);

    private static ChatResponse CallWith(object? argument) =>
        new(new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("c1", "inspect_scene", new Dictionary<string, object?> { ["extra"] = argument }),
        }));

    public static TheoryData<string> ArgumentShapes() => new()
    {
        "uri",
        "uri_in_a_list",
        "string_dictionary",
        "int_keyed_dictionary",
        "record",
        "uri_in_a_nested_dictionary",
        "array_of_uris",
    };

    private static object? Argument(string shape) => shape switch
    {
        "uri" => new Uri("https://example.com/" + Key),
        "uri_in_a_list" => new List<object?> { 1, new Uri("https://example.com/" + Key) },
        "string_dictionary" => new Dictionary<string, string> { ["k"] = Key },
        "int_keyed_dictionary" => new Dictionary<int, string> { [7] = Key },
        "record" => new Credentialed("n", Key),
        "uri_in_a_nested_dictionary" => new Dictionary<string, object?> { ["a"] = new Dictionary<string, object?> { ["b"] = new Uri("https://example.com/" + Key) } },
        "array_of_uris" => new[] { new Uri("https://example.com/" + Key) },
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    [Theory]
    [MemberData(nameof(ArgumentShapes))]
    public async Task D6_a_value_of_any_shape_reaches_a_custom_client_only_as_its_cleaned_text(string shape)
    {
        var chat = new ScriptedChatClient().Enqueue((_, _) => Task.FromResult(CallWith(Argument(shape)))).EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Key });

        var turn = await t.Engine.RunTurnAsync("hello");

        Assert.Equal(TurnStatuses.Completed, turn.Status);
        var sent = CallsIn(chat.Requests[1]).Single();
        var text = Json(sent.Arguments);
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ArgumentShapes))]
    public async Task D6_a_result_of_any_shape_reaches_a_custom_client_only_as_its_cleaned_text(string shape)
    {
        var result = new FunctionResultContent("prior", Argument(shape));
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent> { new TextContent("answer"), result }))))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Key });

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);

        var text = Json(ResultsIn(chat.Requests[1]).Single().Result);
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task D6_the_property_name_of_a_cleaned_value_is_cleaned_too()
    {
        var argument = new Dictionary<string, string> { [Key] = "x" };
        var chat = new ScriptedChatClient().Enqueue((_, _) => Task.FromResult(CallWith(argument))).EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Key });

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("hello")).Status);

        Assert.DoesNotContain(Key, Json(CallsIn(chat.Requests[1]).Single().Arguments), StringComparison.Ordinal);
    }

    [Fact]
    public void D6_messages_with_nothing_to_clean_are_passed_on_untouched()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("c1", "inspect_scene", new Dictionary<string, object?> { ["n"] = 1, ["u"] = new Uri("https://example.com/a") }) }),
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("c1", JsonDocument.Parse("{\"status\":\"success\"}").RootElement.Clone()) }),
        };
        var guard = new SerializedRequestGuard(new ScriptedChatClient(), new Secrets(new[] { Key }));

        var passed = guard.Guarded(messages);

        Assert.Same(messages, passed);
        Assert.Same(messages[1], passed[1]);
    }

    [Fact]
    public void D6_only_the_message_that_held_the_key_is_replaced()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("c1", "inspect_scene", new Dictionary<string, object?> { ["u"] = new Uri("https://example.com/" + Key) }) }),
        };
        var guard = new SerializedRequestGuard(new ScriptedChatClient(), new Secrets(new[] { Key }));

        var passed = guard.Guarded(messages);

        Assert.NotSame(messages, passed);
        Assert.Same(messages[0], passed[0]);
        Assert.NotSame(messages[1], passed[1]);
        Assert.Equal("c1", passed[1].Contents.OfType<FunctionCallContent>().Single().CallId);
        Assert.DoesNotContain(Key, Json(passed[1].Contents), StringComparison.Ordinal);
        // The caller's own objects are not changed.
        Assert.Contains(Key, ((Uri)messages[1].Contents.OfType<FunctionCallContent>().Single().Arguments!["u"]!).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task D6_the_streaming_path_is_guarded_too()
    {
        var inner = new ScriptedChatClient().EnqueueText("done");
        var guard = new SerializedRequestGuard(inner, new Secrets(new[] { Key }));
        var messages = new List<ChatMessage> { new(ChatRole.User, "k " + Key), new(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("c1", "t", new Dictionary<string, object?> { ["u"] = new Uri("https://example.com/" + Key) }) }) };

        await foreach (var _ in guard.GetStreamingResponseAsync(messages, new ChatOptions { Instructions = "rules " + Key })) { }

        var request = Assert.Single(inner.Requests);
        Assert.DoesNotContain(Key, Json(request.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, request.Options!.Instructions!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task D6_a_value_nothing_can_write_is_not_passed_on_and_the_turn_fails_with_provider_error()
    {
        var chat = new ScriptedChatClient().Enqueue((_, _) => Task.FromResult(CallWith(new Unwritable()))).EnqueueText("never reached");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace, knownSecrets: new[] { Key });

        var turn = await t.Engine.RunTurnAsync("hello");

        Assert.Equal(TurnStatuses.Failed, turn.Status);
        Assert.Equal(TurnErrorCodes.ProviderError, turn.ErrorCode);
        Assert.Equal("outbound request rejected: not_serializable", turn.ErrorDetail);
        Assert.Equal(1, chat.RequestCount);
        var line = Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
        Assert.Equal("engine:outbound_rejected", line.Tool);
        Assert.Equal("request not sent: not_serializable", line.Arguments);
    }

    [Fact]
    public async Task D6_a_request_with_more_than_the_value_cap_is_not_passed_on()
    {
        // Each result is under the in-memory limit of one value (10,000 parts); the three together are over the cap of one request.
        var big = JsonDocument.Parse("[" + string.Join(",", Enumerable.Repeat("1", 9_998)) + "]").RootElement.Clone();
        var contents = new List<AIContent> { new TextContent("answer") };
        for (var i = 0; i < 3; i++) contents.Add(new FunctionResultContent("prior" + i, big));
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))))
            .EnqueueText("never reached");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        var turn = await t.Engine.RunTurnAsync("two");

        Assert.Equal(TurnStatuses.Failed, turn.Status);
        Assert.Equal(TurnErrorCodes.ProviderError, turn.ErrorCode);
        Assert.Equal("outbound request rejected: too_many_values", turn.ErrorDetail);
        Assert.Equal(1, chat.RequestCount);
        Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
    }

    [Fact]
    public async Task D6_the_engine_tells_the_guard_of_a_built_client_every_secret_it_knows()
    {
        const string Other = "d6-other-secret-ZZ99-example";
        // A host wrapper that adds a field the engine never sees in its messages: only the byte guard of the client can catch it.
        using var server = new FakeChatServer().EnqueueText("done").EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", Key, "fake-model", server.BaseUrl));
        using var noting = new NotingClient(wire, "note " + Other);

        // Without an engine the guard knows only the client's own key.
        await noting.GetResponseAsync(new List<ChatMessage> { new(ChatRole.User, "hi") });
        Assert.Contains(Other, server.Requests[0].Body, StringComparison.Ordinal);

        // An engine that knows the second secret tells the client's guard, whatever stands between them.
        await using var t = await TestEngine.CreateAsync(chatClient: noting, knownSecrets: new[] { Key, Other });
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10))).Status);
        D6Json.AssertAbsent(server.Requests[1].Body, Other, Key);
        Assert.Contains("note [redacted]", server.Requests[1].Body, StringComparison.Ordinal);
    }

    /// <summary>Adds a JSON-patch string field to every request, as a host wrapper could.</summary>
    private sealed class NotingClient : DelegatingChatClient
    {
        private readonly string _note;

        public NotingClient(IChatClient inner, string note) : base(inner)
        {
            _note = note;
        }

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var changed = options?.Clone() ?? new ChatOptions();
            var previous = changed.RawRepresentationFactory;
            changed.RawRepresentationFactory = client =>
            {
                var raw = previous?.Invoke(client) as ChatCompletionOptions ?? new ChatCompletionOptions();
#pragma warning disable SCME0001
                raw.Patch.Set("$.note"u8, _note);
#pragma warning restore SCME0001
                return raw;
            };
            return base.GetResponseAsync(messages, changed, cancellationToken);
        }
    }
}

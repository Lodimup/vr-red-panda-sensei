using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Integration.CallIds;

/// <summary>
/// S133 (call ids): a call id is a protocol identifier the provider pairs a call with its result by, so the credential pattern (S78) is
/// never applied to one. Known secrets (S126) still are: a call id that holds one is replaced, in the call and in its matching result alike,
/// by one engine-generated id. Message text, arguments and results keep the pattern. Invented values (a made-up key, ids shaped
/// like a live provider's 37-character ones), loopback server only.
/// </summary>
public sealed class CallIdTests
{
    /// <summary>Shaped like the ids a live provider returned: <c>call_</c> and 32 hex characters, 37 in all, letters and digits.</summary>
    private const string IdA = "call_5d0e2b7a91c34f68a2b1d3c4e5f60718";

    private const string IdB = "call_7a3c9e1f40b2486d95a0c1e2d3f4a5b6";

    /// <summary>A known secret that is not itself a credential-pattern match (15 characters).</summary>
    private const string Key = "zq9-example-key";

    /// <summary>A 37-character string the credential pattern matches.</summary>
    private const string Credential = "Zk3pQ9vL2mX8nB4cR7tY1wH5jD6fG0sAeUiOq";

    private static readonly Secrets WithKey = new(new[] { Key });

    // ---- helpers ----

    private static (List<string> Calls, List<string> Results) WireIds(FakeRequest request)
    {
        var messages = request.Json.GetProperty("messages").EnumerateArray().ToList();
        var calls = messages.Where(m => m.TryGetProperty("tool_calls", out _))
            .SelectMany(m => m.GetProperty("tool_calls").EnumerateArray())
            .Select(c => c.GetProperty("id").GetString()!).ToList();
        var results = messages.Where(m => m.GetProperty("role").GetString() == "tool")
            .Select(m => m.GetProperty("tool_call_id").GetString()!).ToList();
        return (calls, results);
    }

    private static (List<string> Calls, List<string> Results) HostIds(RecordedChatRequest request)
    {
        var contents = request.Messages.SelectMany(m => m.Contents).ToList();
        return (contents.OfType<FunctionCallContent>().Select(c => c.CallId).ToList(),
            contents.OfType<FunctionResultContent>().Select(r => r.CallId).ToList());
    }

    /// <summary>Every call has a result under the same id, in the same order, and the ids are all different.</summary>
    private static void AssertPaired((List<string> Calls, List<string> Results) ids, int expected)
    {
        Assert.Equal(expected, ids.Calls.Count);
        Assert.Equal(ids.Calls, ids.Results);
        Assert.Equal(expected, ids.Calls.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids.Calls, id => Assert.False(string.IsNullOrEmpty(id)));
    }

    private static LlmSettings Settings(FakeChatServer server, string key = "call-id-example") =>
        new("openai_compatible", key, "fake-model", server.BaseUrl);

    private static ChatMessage Call(string id, string argumentsJson = "{}") => new(ChatRole.Assistant, new List<AIContent>
    {
        new FunctionCallContent(id, "inspect_scene", JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson)),
    });

    private static ChatMessage Result(string id, object? value = null) => new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent(id, value ?? "ok") });

    private static List<ChatMessage> Exchange(string idA, string idB) => new() { Call(idA), Result(idA), Call(idB), Result(idB) };

    private static List<string> CallIdsOf(IEnumerable<ChatMessage> messages) =>
        messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.CallId).ToList();

    private static List<string> ResultIdsOf(IEnumerable<ChatMessage> messages) =>
        messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.CallId).ToList();

    // ---- (1) real adapter and host client: 37-character ids come back untouched ----

    [Fact]
    public async Task The_real_adapter_sends_the_providers_37_character_call_ids_back_unredacted_and_the_turn_completes()
    {
        Assert.Equal(37, IdA.Length);
        Assert.Equal(37, IdB.Length);
        using var server = new FakeChatServer()
            .EnqueueToolCalls((IdA, "inspect_scene", "{}"), (IdB, "inspect_scene", "{}"))
            .EnqueueText("done")
            .EnqueueText("again");
        using var wire = ChatClientFactory.Create(Settings(server));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);

        var first = await t.Engine.RunTurnAsync("show me the room");
        var second = await t.Engine.RunTurnAsync("and again");

        Assert.Equal(TurnStatuses.Completed, first.Status);
        Assert.Equal(TurnStatuses.Completed, second.Status);
        Assert.Equal(3, server.Requests.Count);
        // The request that follows the calls (turn one, second round) and the next turn's request both carry the same two ids.
        foreach (var request in server.Requests.Skip(1))
        {
            var ids = WireIds(request);
            Assert.Equal(new[] { IdA, IdB }, ids.Calls);
            Assert.Equal(new[] { IdA, IdB }, ids.Results);
            Assert.DoesNotContain("[redacted]", ids.Calls.Concat(ids.Results));
        }
    }

    [Fact]
    public async Task A_host_client_gets_the_providers_37_character_call_ids_back_unredacted_and_the_turn_completes()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls((IdA, "inspect_scene", "{}"), (IdB, "inspect_scene", "{}"))
            .EnqueueText("done")
            .EnqueueText("again");
        await using var t = await TestEngine.CreateAsync(chat: chat);

        var first = await t.Engine.RunTurnAsync("show me the room");
        var second = await t.Engine.RunTurnAsync("and again");

        Assert.Equal(TurnStatuses.Completed, first.Status);
        Assert.Equal(TurnStatuses.Completed, second.Status);
        Assert.Equal(3, chat.RequestCount);
        foreach (var request in chat.Requests.Skip(1))
        {
            var ids = HostIds(request);
            Assert.Equal(new[] { IdA, IdB }, ids.Calls);
            Assert.Equal(new[] { IdA, IdB }, ids.Results);
        }
    }

    // ---- (2) a call id that holds a known secret is replaced, consistently, and the key is gone ----

    [Theory]
    [InlineData("wire, engine knows the key")]
    [InlineData("wire, only the client knows the key")]
    [InlineData("host client")]
    public async Task A_call_id_that_holds_the_known_key_is_replaced_in_call_and_result_with_distinct_ids_and_the_key_is_not_sent(string path)
    {
        var withKeyA = IdA + "-" + Key;
        var withKeyB = IdB + "-" + Key;
        string[] bodies;
        (List<string> Calls, List<string> Results)[] idsPerRequest;
        if (path == "host client")
        {
            var chat = new ScriptedChatClient()
                .EnqueueFunctionCalls((withKeyA, "inspect_scene", "{}"), ("plain-id", "inspect_scene", "{}"), (withKeyB, "inspect_scene", "{}"))
                .EnqueueText("done")
                .EnqueueText("again");
            await using var t = await TestEngine.CreateAsync(chat: chat, knownSecrets: new[] { Key });
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);
            bodies = chat.Requests.Skip(1).Select(r => string.Join("|", r.Messages.SelectMany(m => m.Contents).Select(c => c switch
            {
                FunctionCallContent call => call.CallId + call.Name + string.Join(",", call.Arguments?.Values ?? Array.Empty<object?>()),
                FunctionResultContent result => result.CallId + result.Result,
                TextContent text => text.Text,
                _ => "",
            }))).ToArray();
            idsPerRequest = chat.Requests.Skip(1).Select(HostIds).ToArray();
        }
        else
        {
            using var server = new FakeChatServer()
                .EnqueueToolCalls((withKeyA, "inspect_scene", "{}"), ("plain-id", "inspect_scene", "{}"), (withKeyB, "inspect_scene", "{}"))
                .EnqueueText("done")
                .EnqueueText("again");
            using var wire = ChatClientFactory.Create(Settings(server, Key));
            await using var t = await TestEngine.CreateAsync(chatClient: wire, knownSecrets: path == "wire, engine knows the key" ? new[] { Key } : null);
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
            Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);
            bodies = server.Requests.Skip(1).Select(r => r.Body).ToArray();
            idsPerRequest = server.Requests.Skip(1).Select(WireIds).ToArray();
        }

        Assert.Equal(2, bodies.Length);
        for (var i = 0; i < bodies.Length; i++)
        {
            Assert.DoesNotContain(Key, bodies[i]);
            var ids = idsPerRequest[i];
            AssertPaired(ids, 3);
            Assert.Equal("plain-id", ids.Calls[1]);
            Assert.DoesNotContain("[redacted]", ids.Calls);
            Assert.NotEqual(withKeyA, ids.Calls[0]);
            Assert.NotEqual(withKeyB, ids.Calls[2]);
        }
    }

    [Fact]
    public void The_in_memory_sanitizer_replaces_a_call_id_with_the_known_key_by_one_id_in_call_and_result_and_never_reuses_an_id_in_the_request()
    {
        var withKeyA = IdA + "-" + Key;
        var withKeyB = IdB + "-" + Key;
        // The request already holds an id of the form the generator hands out first: the replacement must be a different one.
        var messages = new List<ChatMessage>(Exchange(withKeyA, withKeyB)) { Call("auto-1"), Result("auto-1") };
        var next = 0;

        var sent = OutboundMessages.Sanitized(messages, WithKey, new List<string>(), engineCallId: () => "auto-" + ++next);

        var calls = CallIdsOf(sent);
        Assert.Equal(calls, ResultIdsOf(sent));
        Assert.Equal(3, calls.Distinct().Count());
        Assert.Equal("auto-1", calls[2]);
        Assert.DoesNotContain("auto-1", calls.Take(2));
        Assert.DoesNotContain(Key, string.Join("|", calls));
        // The stored messages are not changed.
        Assert.Equal(withKeyA, CallIdsOf(messages)[0]);
    }

    [Fact]
    public void The_same_id_in_two_places_gets_the_same_replacement_and_a_result_without_a_call_is_replaced_too()
    {
        var withKey = IdA + "-" + Key;
        var messages = new List<ChatMessage> { Call(withKey), Result(withKey), Result(IdB + "-" + Key) };

        var sent = OutboundMessages.Sanitized(messages, WithKey, new List<string>());

        var results = ResultIdsOf(sent);
        Assert.Equal(CallIdsOf(sent)[0], results[0]);
        Assert.NotEqual(results[0], results[1]);
        Assert.DoesNotContain(Key, string.Join("|", results));
    }

    [Fact]
    public void The_host_guard_replaces_a_call_id_with_the_known_key_by_one_id_in_call_and_result_and_never_reuses_an_id_in_the_request()
    {
        var withKeyA = IdA + "-" + Key;
        var withKeyB = IdB + "-" + Key;
        var messages = new List<ChatMessage>(Exchange(withKeyA, withKeyB)) { Call("auto-1"), Result("auto-1") };
        var next = 0;
        var guard = new SerializedRequestGuard(new ScriptedChatClient(), WithKey, () => "auto-" + ++next);

        var sent = guard.Guarded(messages);

        var calls = CallIdsOf(sent);
        Assert.Equal(calls, ResultIdsOf(sent));
        Assert.Equal(3, calls.Distinct().Count());
        Assert.Equal("auto-1", calls[2]);
        Assert.DoesNotContain(Key, string.Join("|", calls));
        Assert.Equal(withKeyA, CallIdsOf(messages)[0]);
    }

    [Fact]
    public void The_host_guard_without_an_engine_still_replaces_the_id_and_keeps_two_ids_apart()
    {
        var guard = new SerializedRequestGuard(new ScriptedChatClient(), WithKey);

        var sent = guard.Guarded(Exchange(IdA + "-" + Key, IdB + "-" + Key));

        AssertPaired((CallIdsOf(sent), ResultIdsOf(sent)), 2);
        Assert.DoesNotContain(Key, string.Join("|", CallIdsOf(sent)));
    }

    // ---- the wire guard: positions ----

    private static string Body(string callId, string resultId, string text = "hello", string arguments = "{}") =>
        "{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":" + JsonSerializer.Serialize(text) + "},"
        + "{\"role\":\"assistant\",\"tool_calls\":[{\"id\":" + JsonSerializer.Serialize(callId) + ",\"type\":\"function\",\"function\":{\"name\":\"inspect_scene\",\"arguments\":" + JsonSerializer.Serialize(arguments) + "}}]},"
        + "{\"role\":\"tool\",\"tool_call_id\":" + JsonSerializer.Serialize(resultId) + ",\"content\":\"{}\"}]}";

    private static string Guarded(string body, params string[] keys) =>
        Encoding.UTF8.GetString(new OutboundGuard(new Secrets(keys)).Guarded(Encoding.UTF8.GetBytes(body)));

    [Fact]
    public void The_wire_guard_sends_a_request_whose_only_credential_like_strings_are_call_ids_byte_for_byte()
    {
        var bytes = Encoding.UTF8.GetBytes(Body(IdA, IdA));

        var sent = new OutboundGuard(Secrets.None).Guarded(bytes);

        Assert.Same(bytes, sent);
    }

    [Fact]
    public void The_wire_guard_keeps_the_two_call_id_positions_and_nothing_else_of_the_chat_completions_shape()
    {
        var body = Body(IdA, IdB, text: "remember " + Credential, arguments: "{\"note\":\"" + Credential + "\"}");

        var sent = JsonDocument.Parse(Guarded(body)).RootElement;

        var messages = sent.GetProperty("messages").EnumerateArray().ToList();
        var call = messages[1].GetProperty("tool_calls")[0];
        Assert.Equal(IdA, call.GetProperty("id").GetString());
        Assert.Equal(IdB, messages[2].GetProperty("tool_call_id").GetString());
        // Text and arguments keep the pattern.
        Assert.Equal("remember [redacted]", messages[0].GetProperty("content").GetString());
        Assert.Equal("{\"note\":\"[redacted]\"}", call.GetProperty("function").GetProperty("arguments").GetString());
        Assert.DoesNotContain(Credential, sent.GetRawText());
    }

    [Theory]
    [InlineData("{\"id\":\"@\"}")]
    [InlineData("{\"tool_call_id\":\"@\"}")]
    [InlineData("{\"messages\":[{\"role\":\"user\",\"id\":\"@\"}]}")]
    [InlineData("{\"messages\":[{\"role\":\"assistant\",\"tool_calls\":[{\"type\":\"@\"}]}]}")]
    [InlineData("{\"messages\":[{\"role\":\"assistant\",\"tool_calls\":[{\"function\":{\"id\":\"@\"}}]}]}")]
    [InlineData("{\"messages\":[{\"role\":\"assistant\",\"tool_calls\":[{\"function\":{\"name\":\"@\"}}]}]}")]
    [InlineData("{\"messages\":[{\"role\":\"assistant\",\"tool_calls\":[\"@\"]}]}")]
    [InlineData("{\"messages\":[{\"role\":\"assistant\",\"tool_calls\":[[{\"id\":\"@\"}]]}]}")]
    [InlineData("{\"messages\":[{\"role\":\"tool\",\"tool_call_id\":[\"@\"]}]}")]
    [InlineData("{\"messages\":[{\"role\":\"tool\",\"tool_call_id\":{\"id\":\"@\"}}]}")]
    [InlineData("{\"messages\":[\"@\"]}")]
    [InlineData("{\"messages\":{\"tool_call_id\":\"@\"}}")]
    [InlineData("{\"messages\":[[{\"tool_call_id\":\"@\"}]]}")]
    [InlineData("{\"other\":[{\"role\":\"tool\",\"tool_call_id\":\"@\"}]}")]
    [InlineData("{\"x\":{\"messages\":[{\"role\":\"tool\",\"tool_call_id\":\"@\"}]}}")]
    [InlineData("{\"x\":{\"messages\":[{\"tool_calls\":[{\"id\":\"@\"}]}]}}")]
    [InlineData("{\"messages\":[{\"role\":\"tool\",\"content\":\"@\",\"tool_call_id\":\"c1\"}]}")]
    [InlineData("{\"messages\":[{\"tool_calls\":[{\"id\":\"c1\",\"type\":\"@\"}]}]}")]
    [InlineData("{\"messages\":[{\"role\":\"tool\",\"@\":\"c1\"}]}")]
    public void A_credential_like_string_anywhere_but_the_two_call_id_positions_is_still_redacted(string template)
    {
        var sent = Guarded(template.Replace("@", Credential));

        Assert.DoesNotContain(Credential, sent);
        Assert.Contains("[redacted]", sent);
    }

    [Theory]
    [InlineData("{\"messages\":[{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"@\"},{\"id\":\"@@\"}]}]}")]
    [InlineData("{\"messages\":[{\"role\":\"tool\",\"tool_call_id\":\"@\"},{\"role\":\"tool\",\"tool_call_id\":\"@@\"}]}")]
    [InlineData("{\"messages\":[{\"tool_call_id\":\"@\",\"content\":\"x\"}]}")]
    [InlineData("{\"model\":\"m\",\"messages\":[{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"type\":\"function\",\"function\":{\"name\":\"n\",\"arguments\":\"{}\"},\"id\":\"@\"}]}]}")]
    public void A_call_id_is_unredacted_whatever_the_order_of_the_fields_around_it(string template)
    {
        var bytes = Encoding.UTF8.GetBytes(template.Replace("@@", IdB).Replace("@", IdA));

        var sent = new OutboundGuard(Secrets.None).Guarded(bytes);

        Assert.Same(bytes, sent);
    }

    [Fact]
    public void The_wire_guard_replaces_a_call_id_that_holds_the_key_by_one_id_in_call_and_result_and_removes_the_key_everywhere()
    {
        var withKeyA = IdA + "-" + Key;
        var withKeyB = IdB + "-" + Key;
        var body = "{\"messages\":["
                   + "{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"" + withKeyA + "\"},{\"id\":\"" + withKeyB + "\"},{\"id\":\"auto-1\"}]},"
                   + "{\"role\":\"tool\",\"tool_call_id\":\"" + withKeyA + "\",\"content\":\"" + Key + "\"},"
                   + "{\"role\":\"tool\",\"tool_call_id\":\"" + withKeyB + "\"},"
                   + "{\"role\":\"tool\",\"tool_call_id\":\"auto-1\"}]}";

        var sent = JsonDocument.Parse(Guarded(body, Key)).RootElement;

        Assert.DoesNotContain(Key, sent.GetRawText());
        var messages = sent.GetProperty("messages").EnumerateArray().ToList();
        var calls = messages[0].GetProperty("tool_calls").EnumerateArray().Select(c => c.GetProperty("id").GetString()!).ToList();
        var results = messages.Skip(1).Select(m => m.GetProperty("tool_call_id").GetString()!).ToList();
        Assert.Equal(calls, results);
        Assert.Equal(3, calls.Distinct().Count());
        Assert.Equal("auto-1", calls[2]);
        Assert.DoesNotContain("[redacted]", calls);
        Assert.Equal("[redacted]", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public void The_wire_guard_picks_a_replacement_that_no_id_later_in_the_body_already_uses()
    {
        // The replacement is chosen after the whole body has been read: an id that comes after the secret one cannot collide with it.
        var body = "{\"messages\":[{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"" + IdA + "-" + Key + "\"},{\"id\":\"auto-1\"},{\"id\":\"auto-2\"},{\"id\":\"auto-3\"}]}]}";

        var sent = JsonDocument.Parse(Guarded(body, Key)).RootElement;

        var calls = sent.GetProperty("messages")[0].GetProperty("tool_calls").EnumerateArray().Select(c => c.GetProperty("id").GetString()!).ToList();
        Assert.Equal(4, calls.Distinct().Count());
        Assert.Equal(new[] { "auto-1", "auto-2", "auto-3" }, calls.Skip(1));
    }

    // ---- (3) text, arguments and results keep the pattern ----

    [Fact]
    public async Task On_the_real_adapter_a_37_character_credential_like_string_in_text_and_arguments_is_redacted_while_the_call_id_is_not()
    {
        using var server = new FakeChatServer()
            .EnqueueToolCalls((IdA, "inspect_scene", "{\"note\":\"" + Credential + "\"}"))
            .EnqueueText("done");
        using var wire = ChatClientFactory.Create(Settings(server));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);

        var result = await t.Engine.RunTurnAsync("my code is " + Credential);

        Assert.Equal(TurnStatuses.Completed, result.Status);
        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, request => Assert.DoesNotContain(Credential, request.Body));
        Assert.Contains("my code is [redacted]", server.Requests[0].Body);
        var followUp = server.Requests[1];
        var call = followUp.Json.GetProperty("messages").EnumerateArray().Single(m => m.TryGetProperty("tool_calls", out _)).GetProperty("tool_calls")[0];
        Assert.Equal(IdA, call.GetProperty("id").GetString());
        Assert.Contains("[redacted]", call.GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal(IdA, WireIds(followUp).Results.Single());
    }

    [Fact]
    public async Task On_a_host_client_a_37_character_credential_like_string_in_text_and_arguments_is_redacted_while_the_call_id_is_not()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls((IdA, "inspect_scene", "{\"note\":\"" + Credential + "\"}"))
            .EnqueueText("done");
        await using var t = await TestEngine.CreateAsync(chat: chat);

        var result = await t.Engine.RunTurnAsync("my code is " + Credential);

        Assert.Equal(TurnStatuses.Completed, result.Status);
        Assert.Equal(2, chat.RequestCount);
        var followUp = chat.Requests[1].Messages.SelectMany(m => m.Contents).ToList();
        var call = followUp.OfType<FunctionCallContent>().Single();
        Assert.Equal(IdA, call.CallId);
        Assert.DoesNotContain(Credential, JsonSerializer.Serialize(call.Arguments));
        Assert.Contains("[redacted]", JsonSerializer.Serialize(call.Arguments));
        Assert.Equal(IdA, followUp.OfType<FunctionResultContent>().Single().CallId);
        Assert.DoesNotContain(Credential, string.Join("|", followUp.OfType<TextContent>().Select(x => x.Text)));
    }

    [Fact]
    public void The_in_memory_sanitizer_and_the_host_guard_keep_the_pattern_for_text_arguments_and_results_but_not_for_call_ids()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "remember " + Credential),
            Call(IdA, "{\"note\":\"" + Credential + "\"}"),
            Result(IdA, "echo " + Credential),
        };

        var inMemory = OutboundMessages.Sanitized(messages, Secrets.None, new List<string>());
        var host = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None).Guarded(messages);

        foreach (var sent in new[] { inMemory, host })
        {
            var contents = sent.SelectMany(m => m.Contents).ToList();
            Assert.Equal("remember [redacted]", contents.OfType<TextContent>().Single().Text);
            var call = contents.OfType<FunctionCallContent>().Single();
            Assert.Equal(IdA, call.CallId);
            Assert.DoesNotContain(Credential, JsonSerializer.Serialize(call.Arguments));
            Assert.Contains("[redacted]", JsonSerializer.Serialize(call.Arguments));
            var result = contents.OfType<FunctionResultContent>().Single();
            Assert.Equal(IdA, result.CallId);
            Assert.DoesNotContain(Credential, JsonSerializer.Serialize(result.Result));
            Assert.Contains("[redacted]", JsonSerializer.Serialize(result.Result));
        }
    }

    [Fact]
    public void A_call_id_is_not_exempt_from_a_known_secret_that_is_the_whole_id()
    {
        var sent = OutboundMessages.Sanitized(Exchange(Key, IdB), WithKey, new List<string>());

        var calls = CallIdsOf(sent);
        Assert.NotEqual(Key, calls[0]);
        Assert.Equal(IdB, calls[1]);
        Assert.Equal(calls, ResultIdsOf(sent));
    }
}

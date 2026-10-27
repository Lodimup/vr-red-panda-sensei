using System.ClientModel;
using System.Text;
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

namespace Sensei.Engine.Tests.Integration.ReviewFinal;

/// <summary>S130: the provider sees only text, function calls and function results, every string in them sanitized.</summary>
public sealed class OutboundAllowListTests
{
    private const string Secret = "final-ruling-key-QX42";

    private static readonly Secrets Known = new(new[] { Secret });

    private static string Json(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages) =>
        JsonSerializer.Serialize(messages.ToList(), AIJsonUtilities.DefaultOptions);

    [Fact]
    public async Task S130_text_function_calls_and_function_results_still_reach_the_provider()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "inspect_scene", "{}")).EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Secret });

        await t.Engine.RunTurnAsync("hello " + Secret);

        var second = chat.Requests[1];
        var contents = second.Messages.SelectMany(m => m.Contents).ToList();
        Assert.Contains(contents, c => c is FunctionCallContent call && call.CallId == "c1" && call.Name == "inspect_scene");
        Assert.Contains(contents, c => c is FunctionResultContent result && result.CallId == "c1");
        Assert.Contains(contents, c => c is TextContent text && text.Text.Contains("hello", StringComparison.Ordinal));
        foreach (var request in chat.Requests)
        {
            Assert.All(request.Messages.SelectMany(m => m.Contents),
                c => Assert.True(c is TextContent or FunctionCallContent or FunctionResultContent, c.GetType().FullName));
            Assert.DoesNotContain(Secret, Everything(request));
        }
    }

    private static AIContent Foreign(string kind) => kind switch
    {
        "reasoning" => new TextReasoningContent("thinking " + Secret),
        "error" => new ErrorContent("provider echoed " + Secret),
        "uri" => new UriContent("https://example.com/" + Secret, "image/png"),
        _ => new DataContent(Encoding.UTF8.GetBytes("raw " + Secret), "image/png"),
    };

    [Theory]
    [InlineData("reasoning")]
    [InlineData("error")]
    [InlineData("uri")]
    [InlineData("data")]
    public async Task S130_a_content_type_outside_the_allow_list_is_removed_and_traced_by_type_name(string kind)
    {
        var foreign = Foreign(kind);
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, new List<AIContent> { new TextContent("answer"), foreign }))))
            .EnqueueText("done");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace, knownSecrets: new[] { Secret });

        await t.Engine.RunTurnAsync("one");
        await t.Engine.RunTurnAsync("two");

        var assistant = chat.Requests[1].Messages.Single(m => m.Role == ChatRole.Assistant);
        Assert.Equal("answer", Assert.IsType<TextContent>(Assert.Single(assistant.Contents)).Text);
        Assert.All(chat.Requests[1].Messages.SelectMany(m => m.Contents), c => Assert.IsNotType(foreign.GetType(), c));
        var note = Assert.Single(trace.Entries, e => e.Tool == "engine:outbound_content");
        Assert.True(note.Arguments.Contains(foreign.GetType().FullName!, StringComparison.Ordinal), kind + ": " + note.Arguments);
        Assert.Equal(ToolErrorCodes.OutboundContentRemoved, note.ErrorCode);
        Assert.DoesNotContain(Secret, note.Arguments);
    }

    [Fact]
    public void S130_a_message_left_without_content_by_the_allow_list_is_dropped()
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.User, "question"),
            new(ChatRole.Assistant, new List<AIContent> { new UriContent("https://example.com/x", "image/png") }),
            new(ChatRole.Assistant, new List<AIContent>()),
        };
        var notes = new List<string>();

        var sent = OutboundMessages.Sanitized(messages, Known, notes);

        Assert.Equal(2, sent.Count);
        Assert.Equal(ChatRole.User, sent[0].Role);
        Assert.Empty(sent[1].Contents);
        Assert.Equal(new[] { "removed " + typeof(UriContent).FullName }, notes);
    }

    [Fact]
    public void S130_every_string_of_an_allowed_type_is_sanitized()
    {
        var arguments = new Dictionary<string, object?>
        {
            ["plain"] = "see " + Secret,
            [Secret] = new List<object?> { "a", new Dictionary<string, object?> { ["deep"] = Secret }, JsonDocument.Parse("{\"x\":\"" + Secret + "\"}").RootElement.Clone() },
            ["node"] = new JsonObject { ["y"] = Secret },
            ["number"] = 7,
        };
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.User, "q " + Secret) { AuthorName = Secret, MessageId = Secret },
            new(ChatRole.Assistant, new List<AIContent> { new TextContent("t " + Secret), new FunctionCallContent("id-" + Secret, "tool-" + Secret, arguments) }),
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("id-" + Secret, new Dictionary<string, object?> { ["r"] = new[] { Secret } }) }),
        };

        var sent = OutboundMessages.Sanitized(messages, Known, new List<string>());

        Assert.DoesNotContain(Secret, Json(sent));
        var call = (FunctionCallContent)sent[1].Contents[1];
        Assert.Equal(7, call.Arguments!["number"]);
        Assert.Equal("see [redacted]", call.Arguments["plain"]);
    }

    [Fact]
    public void S130_argument_values_deeper_than_32_levels_are_sanitized_not_forwarded()
    {
        object? value = Secret;
        for (var i = 0; i < 300; i++) value = i % 2 == 0 ? new List<object?> { value } : new Dictionary<string, object?> { ["k"] = value };
        var call = new FunctionCallContent("c1", "inspect_scene", new Dictionary<string, object?> { ["deep"] = value });
        var messages = new List<Microsoft.Extensions.AI.ChatMessage> { new(ChatRole.Assistant, new List<AIContent> { call }) };
        var notes = new List<string>();

        var sent = OutboundMessages.Sanitized(messages, Known, notes);

        var clean = (FunctionCallContent)sent[0].Contents[0];
        Assert.Empty(clean.Arguments!);
        Assert.Equal("arguments nested over 32 levels (replaced by an empty object)", Assert.Single(notes));
    }

    [Fact]
    public void S130_a_cyclic_value_is_replaced_not_walked_forever()
    {
        var loop = new Dictionary<string, object?>();
        loop["self"] = loop;
        var call = new FunctionCallContent("c1", "inspect_scene", loop);
        var notes = new List<string>();

        var sent = OutboundMessages.Sanitized(new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, new List<AIContent> { call }) }, Known, notes);

        Assert.Empty(((FunctionCallContent)sent[0].Contents[0]).Arguments!);
        Assert.Single(notes);
        Assert.Contains("replaced", notes[0], StringComparison.Ordinal);
    }

    [Fact]
    public void S130_a_value_of_exactly_the_node_limit_is_kept_and_one_more_is_replaced()
    {
        // The list itself is one node; its items are the rest.
        var atLimit = Enumerable.Repeat<object?>("x " + Secret, JsonRedaction.MaxNodes - 1).ToList();
        var overLimit = Enumerable.Repeat<object?>("x " + Secret, JsonRedaction.MaxNodes).ToList();

        var kept = (List<object?>)JsonRedaction.SanitizedValue(atLimit, Known, out var keptOversized)!;
        var replaced = JsonRedaction.SanitizedValue(overLimit, Known, out var replacedOversized);

        Assert.False(keptOversized);
        Assert.Equal(JsonRedaction.MaxNodes - 1, kept.Count);
        Assert.All(kept, item => Assert.Equal("x [redacted]", item));
        Assert.True(replacedOversized);
        Assert.Equal(JsonRedaction.OversizedValue, replaced);
    }

    [Fact]
    public async Task S130_loopback_text_survives_while_the_image_is_not_sent()
    {
        using var server = new FakeChatServer().EnqueueJson(200, """
        {"id":"chatcmpl-image","object":"chat.completion","created":1700000000,"model":"fake-model","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":[{"type":"text","text":"answer for the learner"},{"type":"image_url","image_url":{"url":"https://example.com/picture.png"}}]}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
        """).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { Secret });

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one").WaitAsync(TimeSpan.FromSeconds(10))).Status);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two").WaitAsync(TimeSpan.FromSeconds(10))).Status);

        var body = server.Requests[1].Body;
        Assert.Contains("answer for the learner", body, StringComparison.Ordinal);
        Assert.DoesNotContain("image_url", body, StringComparison.Ordinal);
        Assert.DoesNotContain("picture.png", body, StringComparison.Ordinal);
    }
}

/// <summary>S131: tool arguments reach a trace only as the parser unescaped and the sanitizer cleaned them, or not at all.</summary>
public sealed class TraceArgumentsTests
{
    private const string Secret = "final-ruling-key-QX42";

    private static readonly Secrets Known = new(new[] { Secret });
    private const string Replaced = "\"[unparseable arguments]\"";

    private static string Escaped(string text) => text.Replace("-", "\\u002d");

    [Fact]
    public void S131_a_shallow_object_is_written_again_with_values_and_names_sanitized_and_everything_else_kept()
    {
        var text = JsonRedaction.SanitizedArgumentsText(
            "{ \"n\": 12345678901234567890, \"f\": -0.5e+3, \"t\": true, \"u\": null, \"s\": \"" + Escaped(Secret) + "\", \"" + Escaped(Secret) + "\": [1, 2] }", Known);

        Assert.Equal("{\"n\":12345678901234567890,\"f\":-0.5e+3,\"t\":true,\"u\":null,\"s\":\"[redacted]\",\"[redacted]\":[1,2]}", text);
    }

    [Fact]
    public void S131_duplicate_property_names_are_kept()
    {
        Assert.Equal("{\"a\":1,\"a\":2}", JsonRedaction.SanitizedArgumentsText("{\"a\":1,\"a\":2}", Known));
    }

    [Fact]
    public void S131_a_secret_written_with_unicode_escapes_for_ordinary_letters_is_still_found()
    {
        var spelled = string.Concat(Secret.Select(c => "\\u" + ((int)c).ToString("x4")));

        Assert.Equal("[\"[redacted]\"]", JsonRedaction.SanitizedArgumentsText("[\"" + spelled + "\"]", Known));
    }

    [Theory]
    [InlineData(34)]
    [InlineData(500)]
    [InlineData(5000)]
    public void S131_a_deep_but_valid_document_is_sanitized_not_replaced(int depth)
    {
        var json = new string('[', depth) + "\"" + Escaped(Secret) + "\"" + new string(']', depth);

        var text = JsonRedaction.SanitizedArgumentsText(json, Known);

        Assert.Equal(new string('[', depth) + "\"[redacted]\"" + new string(']', depth), text);
    }

    [Fact]
    public void S131_the_node_limit_is_exact()
    {
        var atLimit = "[" + string.Join(",", Enumerable.Repeat("1", JsonRedaction.MaxNodes - 1)) + "]";
        var overLimit = "[" + string.Join(",", Enumerable.Repeat("1", JsonRedaction.MaxNodes)) + "]";

        Assert.Equal(atLimit, JsonRedaction.SanitizedArgumentsText(atLimit, Known));
        Assert.Equal(Replaced, JsonRedaction.SanitizedArgumentsText(overLimit, Known));
        Assert.Equal(Replaced, JsonRedaction.SanitizedArgumentsText(new string('[', 20000) + new string(']', 20000), Known));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1,2,]")]
    [InlineData("{\"a\":1} trailing")]
    [InlineData("{\"a\":1}{\"b\":2}")]
    [InlineData("// comment\n{}")]
    [InlineData("{\"a\": \"\\ud800\"}")]
    [InlineData("{'a': 1}")]
    [InlineData("not json final-ruling-key-QX42")]
    public void S131_text_that_does_not_parse_is_replaced_as_a_whole_and_never_returned(string json)
    {
        var text = JsonRedaction.SanitizedArgumentsText(json, Known);

        Assert.Equal(Replaced, text);
        Assert.Equal("[unparseable arguments]", JsonSerializer.Deserialize<string>(text));
    }

    [Fact]
    public void S131_a_raw_lone_surrogate_is_replaced()
    {
        Assert.Equal(Replaced, JsonRedaction.SanitizedArgumentsText("{\"a\":\"" + '\ud800' + "\"}", Known));
    }

    [Theory]
    [InlineData("null", "null")]
    [InlineData("42", "42")]
    [InlineData("true", "true")]
    [InlineData("\"plain\"", "\"plain\"")]
    public void S131_a_scalar_document_is_a_valid_argument_text_too(string json, string expected)
    {
        Assert.Equal(expected, JsonRedaction.SanitizedArgumentsText(json, Known));
    }

    [Fact]
    public async Task S131_the_trace_of_a_call_the_schema_parser_rejects_for_depth_is_sanitized_all_the_way_down()
    {
        var depth = 400;
        var json = new string('[', depth) + "\"" + Escaped(Secret) + "\"" + new string(']', depth);
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(trace: trace, knownSecrets: new[] { Secret }, extraTools: new SenseiTool[] { new ProbeTool() });
        t.Engine.BeginTurn("x");

        var result = await t.CallAsync("test_probe", json);

        Assert.Equal(ToolErrorCodes.InvalidArguments, result.ErrorCode);
        Assert.Equal(new string('[', depth) + "\"[redacted]\"" + new string(']', depth), Assert.Single(trace.Entries).Arguments);
    }

    [Fact]
    public async Task S131_the_trace_of_an_over_limit_call_holds_the_replacement()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat("\"" + Escaped(Secret) + "\"", JsonRedaction.MaxNodes)) + "]";
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(trace: trace, knownSecrets: new[] { Secret }, extraTools: new SenseiTool[] { new ProbeTool() });
        t.Engine.BeginTurn("x");

        var result = await t.CallAsync("test_probe", json);

        Assert.Equal(ToolErrorCodes.InvalidArguments, result.ErrorCode);
        Assert.Equal(Replaced, Assert.Single(trace.Entries).Arguments);
    }

    [Fact]
    public async Task S131_the_cli_trace_file_never_holds_malformed_provider_text()
    {
        var raw = "{\"action\": \"" + Secret + "\" oops";
        using var server = new FakeChatServer().EnqueueToolCalls(("bad", "inspect_scene", raw)).EnqueueText("done");

        var result = await InProcessCli.ScriptAsync(new[] { "hello" }, env: CliRunner.EnvFor(server, Secret));

        Assert.Equal(0, result.Code);
        var path = Assert.Single(Directory.GetFiles(Path.Combine(result.ProfileDir, "traces"), "*.jsonl"));
        var file = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain(Secret, file, StringComparison.Ordinal);
        Assert.DoesNotContain("oops", file, StringComparison.Ordinal);
        using var line = JsonDocument.Parse(Assert.Single(file.Split('\n', StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal(Replaced, line.RootElement.GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task S131_the_cli_trace_file_holds_the_sanitized_form_of_valid_arguments()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(("ok", "inspect_scene", "{\"note\":\"" + Escaped(Secret) + "\"}")).EnqueueText("done");

        var result = await InProcessCli.ScriptAsync(new[] { "hello" }, env: CliRunner.EnvFor(server, Secret));

        Assert.Equal(0, result.Code);
        var path = Assert.Single(Directory.GetFiles(Path.Combine(result.ProfileDir, "traces"), "*.jsonl"));
        using var line = JsonDocument.Parse(Assert.Single(await File.ReadAllLinesAsync(path)));
        Assert.Equal("{\"note\":\"[redacted]\"}", line.RootElement.GetProperty("arguments").GetString());
    }
}

/// <summary>S132: an HTTP status is read from exactly one place, the <c>Status</c> of an exact <see cref="ClientResultException"/>.</summary>
public sealed class ProviderStatusTests
{
    private static async Task<TurnResult> FailWith(Exception error)
    {
        var chat = new ScriptedChatClient().EnqueueError(error);
        await using var t = await EngineAsync(chat);
        return await t.Engine.RunTurnAsync("hello");
    }

    [Fact]
    public async Task S132_a_401_answered_over_loopback_to_the_real_client_is_auth_failed()
    {
        using var server = new FakeChatServer().EnqueueStatus(401);
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", "final-ruling-key-QX42", "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { "final-ruling-key-QX42" });

        var result = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal(TurnErrorCodes.AuthFailed, result.ErrorCode);
        Assert.StartsWith("HTTP 401", result.ErrorDetail, StringComparison.Ordinal);
        Assert.Single(server.Requests);
    }

    [Theory]
    [InlineData(401, TurnErrorCodes.AuthFailed)]
    [InlineData(403, TurnErrorCodes.AuthFailed)]
    [InlineData(402, TurnErrorCodes.QuotaExceeded)]
    [InlineData(429, TurnErrorCodes.QuotaExceeded)]
    [InlineData(500, TurnErrorCodes.ProviderError)]
    public async Task S132_a_client_result_exception_status_decides_the_code(int status, string code)
    {
        var result = await FailWith(new ClientResultException("failed", new FakePipelineResponse(status)));

        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public async Task S132_the_status_of_an_exact_client_result_exception_is_found_inside_wrappers()
    {
        var inner = new ClientResultException("denied", new FakePipelineResponse(401));
        var result = await FailWith(new InvalidOperationException("outer", new AggregateException(new TimeoutException("slow"), inner)));

        Assert.Equal(TurnErrorCodes.AuthFailed, result.ErrorCode);
    }

    [Fact]
    public async Task S132_a_subclass_of_client_result_exception_is_not_read()
    {
        var error = new ShadowedClientResultException();

        var result = await FailWith(error);

        Assert.Equal(TurnErrorCodes.ProviderError, result.ErrorCode);
        Assert.Equal(0, error.GetterReads);
    }

    [Theory]
    [InlineData(401, TurnErrorCodes.AuthFailed)]
    [InlineData(403, TurnErrorCodes.AuthFailed)]
    [InlineData(402, TurnErrorCodes.QuotaExceeded)]
    [InlineData(429, TurnErrorCodes.QuotaExceeded)]
    [InlineData(500, TurnErrorCodes.ProviderError)]
    public async Task S132_an_exact_http_request_exception_with_a_status_maps_by_that_status(int status, string code)
    {
        var result = await FailWith(new HttpRequestException("failed", null, (System.Net.HttpStatusCode)status));

        Assert.Equal(code, result.ErrorCode);
        Assert.StartsWith("HTTP " + status, result.ErrorDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task S132_an_exact_http_request_exception_without_a_status_is_a_network_error()
    {
        var result = await FailWith(new HttpRequestException("no route"));

        Assert.Equal(TurnErrorCodes.NetworkError, result.ErrorCode);
        Assert.DoesNotContain("HTTP ", result.ErrorDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task S132_an_exact_http_request_exception_inside_a_wrapper_is_read_too()
    {
        var inner = new HttpRequestException("denied", null, System.Net.HttpStatusCode.Unauthorized);
        var result = await FailWith(new InvalidOperationException("outer", new AggregateException(new TimeoutException("slow"), inner)));

        Assert.Equal(TurnErrorCodes.AuthFailed, result.ErrorCode);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(402)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task S132_a_subclass_of_http_request_exception_with_the_same_status_is_a_network_error_without_a_member_read(int status)
    {
        var shadow = new ShadowedHttpRequestException(status);
        var plain = new PlainSubclassHttpRequestException(status);

        var shadowed = await FailWith(shadow);
        var unshadowed = await FailWith(plain);

        Assert.Equal(TurnErrorCodes.NetworkError, shadowed.ErrorCode);
        Assert.Equal(TurnErrorCodes.NetworkError, unshadowed.ErrorCode);
        Assert.Equal(0, shadow.GetterReads);
    }

    [Fact]
    public void S132_the_source_has_one_reflective_read_and_it_is_the_status_of_the_framework_http_request_exception()
    {
        var source = File.ReadAllText(SourcePath("Agent", "ProviderErrors.cs"));

        Assert.Equal(1, Occurrences(source, ".GetProperty("));
        Assert.Contains("typeof(HttpRequestException).GetProperty(\"StatusCode\")", source, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(source, ".GetValue("));
        Assert.DoesNotContain("GetType().GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMethod", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Invoke(", source, StringComparison.Ordinal);
        Assert.Contains("node.GetType() == typeof(HttpRequestException)", source, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string SourcePath(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var root = Path.Combine(dir.FullName, "sensei-engine", "src", "Sensei.Engine");
            if (Directory.Exists(root)) return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
        throw new DirectoryNotFoundException("sensei-engine/src/Sensei.Engine not found above " + AppContext.BaseDirectory);
    }

    private sealed class ShadowedClientResultException : ClientResultException
    {
        public int GetterReads;

        public ShadowedClientResultException() : base("denied", new FakePipelineResponse(401))
        {
        }

        public new int Status
        {
            get
            {
                Interlocked.Increment(ref GetterReads);
                throw new InvalidOperationException("foreign getter");
            }
        }
    }

    private sealed class PlainSubclassHttpRequestException : HttpRequestException
    {
        public PlainSubclassHttpRequestException(int status) : base("denied", null, (System.Net.HttpStatusCode)status)
        {
        }
    }

    private sealed class ShadowedHttpRequestException : HttpRequestException
    {
        public int GetterReads;

        public ShadowedHttpRequestException(int status) : base("denied", null, (System.Net.HttpStatusCode)status)
        {
        }

        public new System.Net.HttpStatusCode? StatusCode
        {
            get
            {
                Interlocked.Increment(ref GetterReads);
                throw new InvalidOperationException("foreign getter");
            }
        }
    }
}

/// <summary>S61, S25, S85: calls the adapter drops for a blank name come back where the provider put them.</summary>
public sealed class RecoveredCallOrderTests
{
    private static IChatClient Client(FakeChatServer server) =>
        ChatClientFactory.Create(new LlmSettings("openai_compatible", "final-ruling-key-QX42", "fake-model", server.BaseUrl));

    [Fact]
    public async Task S85_a_blank_name_between_valid_calls_keeps_its_place()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("first", "inspect_scene", "{}"), ("blank", " ", "{}"), ("last", "inspect_scene", "{}")).EnqueueText("done");
        using var client = Client(server);
        await using var t = await TestEngine.CreateAsync(chatClient: client);

        var result = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "first", "blank", "last" }, result.ToolCalls.Select(c => c.CallId));
        Assert.Equal(new[] { ToolStatuses.Success, ToolStatuses.Failure, ToolStatuses.Success }, result.ToolCalls.Select(c => c.Status));
        Assert.Equal(ToolErrorCodes.UnknownTool, result.ToolCalls[1].ErrorCode);
    }

    [Fact]
    public async Task S85_two_blank_names_at_the_end_after_a_spawn_run_after_it()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("spawn", "spawn_objects", """{"objects":[{"catalog_id":"cup"}]}"""), ("blank1", "", "{}"), ("blank2", "", "{}")).EnqueueText("done");
        using var client = Client(server);
        await using var t = await TestEngine.CreateAsync(chatClient: client);

        var result = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "spawn", "blank1", "blank2" }, result.ToolCalls.Select(c => c.CallId));
        Assert.Equal(ToolStatuses.Success, result.ToolCalls[0].Status);
        Assert.Single(t.Engine.GetScene().Instances);
    }

    [Fact]
    public async Task S85_a_blank_name_first_counts_before_the_call_after_it_and_the_second_one_exhausts_the_turn()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("blank", "", "{}"), ("unknown", "no_such_tool", "{}"), ("spawn", "spawn_objects", """{"objects":[{"catalog_id":"cup"}]}""")).EnqueueText("done");
        using var client = Client(server);
        await using var t = await TestEngine.CreateAsync(chatClient: client);

        var result = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "blank", "unknown", "spawn" }, result.ToolCalls.Select(c => c.CallId));
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, result.ToolCalls[2].ErrorCode);
        Assert.Empty(t.Engine.GetScene().Instances);
    }

    [Fact]
    public async Task S85_assistant_text_stays_before_the_calls_when_a_blank_one_is_recovered()
    {
        var body = new JsonObject
        {
            ["id"] = "chatcmpl-order", ["object"] = "chat.completion", ["created"] = 1700000000, ["model"] = "fake-model",
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0, ["finish_reason"] = "tool_calls",
                ["message"] = new JsonObject
                {
                    ["role"] = "assistant", ["content"] = "looking now",
                    ["tool_calls"] = new JsonArray(
                        Call("first", "inspect_scene"), Call("blank", ""), Call("last", "inspect_scene")),
                },
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 1, ["completion_tokens"] = 1, ["total_tokens"] = 2 },
        };
        using var server = new FakeChatServer().EnqueueJson(200, body.ToJsonString()).EnqueueText("done");
        using var client = Client(server);
        await using var t = await TestEngine.CreateAsync(chatClient: client);

        var result = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "first", "blank", "last" }, result.ToolCalls.Select(c => c.CallId));
        Assert.Contains("looking now", result.UnspokenText, StringComparison.Ordinal);
    }

    private static JsonObject Call(string id, string name) => new()
    {
        ["id"] = id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = "{}" },
    };
}

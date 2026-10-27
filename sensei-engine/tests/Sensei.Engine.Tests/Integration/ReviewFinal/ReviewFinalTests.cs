using Microsoft.Extensions.AI;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.Cli;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Integration.ReviewFinal;

public sealed class ReviewFinalTests
{
    private const string Secret = "review-final-key-QX42";

    [Fact]
    public async Task F01_recovered_blank_calls_preserve_provider_order_and_exhaust_before_spawn()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("blank1", "", "{}"), ("blank2", " ", "{}"),
            ("spawn", "spawn_objects", """{"objects":[{"catalog_id":"cup"}]}""")).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { Secret });
        var result = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "blank1", "blank2", "spawn" }, result.ToolCalls.Select(c => c.CallId));
        Assert.Empty(t.Engine.GetScene().Instances);
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, result.ToolCalls.Last().ErrorCode);
    }

    [Fact]
    public async Task Probe_unknown_tool_secret_is_absent_from_options()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("call1", "tool_" + Secret, "{}")).EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Secret });
        await t.Engine.RunTurnAsync("hello");
        foreach (var r in chat.Requests)
            Assert.DoesNotContain(Secret, string.Join("|", r.Options?.Tools?.Select(tool => tool.Name) ?? Array.Empty<string>()));
    }

    [Fact]
    public async Task Probe_null_arguments_are_invalid_not_empty_object()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(("call1", "inspect_scene", "null")).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client);
        var result = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ToolErrorCodes.InvalidArguments, Assert.Single(result.ToolCalls).ErrorCode);
    }
}

public sealed class MoreReviewFinalTests
{
    private const string Secret = "review-final-key-QX42";

    [Theory]
    [InlineData("error")]
    [InlineData("uri")]
    public async Task F02_other_content_types_must_not_resend_known_key(string kind)
    {
        AIContent content = kind == "error"
            ? new ErrorContent("provider echoed " + Secret)
            : new UriContent("https://example.com/" + Secret, "image/png");
        var chat = new ScriptedChatClient().Enqueue((_, _) => Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, new List<AIContent> { new TextContent("answer"), content })))).EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Secret });
        await t.Engine.RunTurnAsync("one");
        await t.Engine.RunTurnAsync("two");
        var outgoing = chat.Requests[1].Messages.SelectMany(m => m.Contents);
        foreach (var c in outgoing)
        {
            if (c is ErrorContent e) Assert.DoesNotContain(Secret, e.Message);
            if (c is UriContent u) Assert.DoesNotContain(Secret, u.Uri.AbsoluteUri);
        }
    }

    [Fact]
    public async Task F03_deep_escaped_arguments_must_not_store_a_recoverable_key_in_trace()
    {
        // A valid 33-level JSON value exceeds the pipeline's 32-level argument parser.
        var escaped = Secret.Replace("-", "\\u002d");
        var args = new string('[', 33) + "\"" + escaped + "\"" + new string(']', 33);
        using var server = new FakeChatServer().EnqueueToolCalls(("deep", "inspect_scene", args)).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { Secret });
        var result = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));
        var entry = Assert.Single(result.ToolCalls);
        Assert.Equal(ToolErrorCodes.InvalidArguments, entry.ErrorCode);
        using var stored = System.Text.Json.JsonDocument.Parse(entry.Arguments);
        var value = stored.RootElement;
        for (var i = 0; i < 33; i++) value = value[0];
        Assert.DoesNotContain(Secret, value.GetString());
    }

    [Fact]
    public async Task F04_empty_scene_must_print_the_S128_header()
    {
        var result = await InProcessCli.ScriptAsync(new[] { "/scene" });
        Assert.Equal(0, result.Code);
        Assert.Equal("id | catalog | region | slot | in | japanese | reading | english", result.Lines[1]);
    }

    [Fact]
    public async Task F05_provider_status_reads_must_not_execute_a_subclass_getter()
    {
        var error = new ShadowStatusException();
        var chat = new ScriptedChatClient().EnqueueError(error);
        await using var t = await EngineAsync(chat);
        var result = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(TurnErrorCodes.NetworkError, result.ErrorCode);
        Assert.Equal(0, error.GetterReads);
    }

    private sealed class ShadowStatusException : HttpRequestException
    {
        public int GetterReads;
        public ShadowStatusException() : base("denied", null, System.Net.HttpStatusCode.Unauthorized) { }
        public new System.Net.HttpStatusCode? StatusCode
        {
            get { Interlocked.Increment(ref GetterReads); throw new InvalidOperationException("foreign getter"); }
        }
    }
}

public sealed class BoundaryReviewFinalTests
{
    private const string Secret = "review-final-key-QX42";

    [Fact]
    public async Task F01_reordered_calls_allow_a_spawn_that_should_be_over_budget()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("blank1", "", "{}"), ("blank2", " ", "{}"),
            ("spawn", "spawn_objects", """{"objects":[{"catalog_id":"cup"}]}""")).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client);
        var result = await t.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(t.Engine.GetScene().Instances.Count == 0,
            $"Expected no mutation after two invalid calls; actual revision={t.Engine.GetScene().Revision}, " +
            $"objects={string.Join(',', t.Engine.GetScene().Instances.Select(i => i.Id))}, " +
            $"trace={string.Join(',', result.ToolCalls.Select(c => c.CallId + ':' + c.Status))}");
    }

    [Fact]
    public async Task F03_cli_trace_file_must_not_persist_a_recoverable_escaped_key()
    {
        var args = new string('[', 33) + "\"" + Secret.Replace("-", "\\u002d") + "\"" + new string(']', 33);
        using var server = new FakeChatServer().EnqueueToolCalls(("deep", "inspect_scene", args)).EnqueueText("done");
        var result = await InProcessCli.ScriptAsync(new[] { "hello" }, env: CliRunner.EnvFor(server, Secret));
        Assert.Equal(0, result.Code);
        var path = Assert.Single(Directory.GetFiles(Path.Combine(result.ProfileDir, "traces"), "*.jsonl"));
        using var line = System.Text.Json.JsonDocument.Parse(Assert.Single(await File.ReadAllLinesAsync(path)));
        using var stored = System.Text.Json.JsonDocument.Parse(line.RootElement.GetProperty("arguments").GetString()!);
        var value = stored.RootElement;
        for (var i = 0; i < 33; i++) value = value[0];
        Assert.DoesNotContain(Secret, value.GetString());
    }

    [Fact]
    public async Task F05_provider_status_getter_must_not_hang_the_engine()
    {
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, "dotnet"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(BoundaryReviewFinalTests).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName~BoundaryReviewFinalTests.StatusGetterChildProbe");
        start.Environment["RPS_REVIEW_FINAL_STATUS"] = "hang";
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail("Provider error mapping hung in a subclass StatusCode getter for 5 seconds; child testhost killed.");
        }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    [Fact]
    public async Task StatusGetterChildProbe()
    {
        if (Environment.GetEnvironmentVariable("RPS_REVIEW_FINAL_STATUS") is null) return;
        var chat = new ScriptedChatClient().EnqueueError(new BlockingStatusException());
        await using var t = await EngineAsync(chat);
        var result = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(TurnErrorCodes.NetworkError, result.ErrorCode);
    }

    private sealed class BlockingStatusException : HttpRequestException
    {
        public BlockingStatusException() : base("denied", null, System.Net.HttpStatusCode.Unauthorized) { }
        public new System.Net.HttpStatusCode? StatusCode
        {
            get { Thread.Sleep(Timeout.Infinite); return null; }
        }
    }
}

public sealed class ReviewFinalControls
{
    private const string Secret = "review-final-key-QX42";

    [Fact]
    public async Task Named_unknown_calls_preserve_order_and_prevent_over_budget_spawn()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(
            ("unknown1", "unknown_one", "{}"), ("unknown2", "unknown_two", "{}"),
            ("spawn", "spawn_objects", """{"objects":[{"catalog_id":"cup"}]}""")).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client);
        var result = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(new[] { "unknown1", "unknown2", "spawn" }, result.ToolCalls.Select(c => c.CallId));
        Assert.Empty(t.Engine.GetScene().Instances);
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, result.ToolCalls.Last().ErrorCode);
    }

    [Fact]
    public async Task Shallow_escaped_arguments_are_redacted_in_trace()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(("shallow", "inspect_scene", "{\"x\":\"" + Secret.Replace("-", "\\u002d") + "\"}")).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", Secret, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { Secret });
        var result = await t.Engine.RunTurnAsync("hello");
        using var doc = System.Text.Json.JsonDocument.Parse(Assert.Single(result.ToolCalls).Arguments);
        Assert.Equal("[redacted]", doc.RootElement.GetProperty("x").GetString());
    }

    [Theory]
    [InlineData(401, TurnErrorCodes.AuthFailed)]
    [InlineData(403, TurnErrorCodes.AuthFailed)]
    [InlineData(402, TurnErrorCodes.QuotaExceeded)]
    [InlineData(429, TurnErrorCodes.QuotaExceeded)]
    [InlineData(500, TurnErrorCodes.ProviderError)]
    public async Task Ordinary_http_statuses_still_map_correctly(int status, string code)
    {
        var chat = new ScriptedChatClient().EnqueueError(new System.ClientModel.ClientResultException("ordinary failure", new FakePipelineResponse(status)));
        await using var t = await EngineAsync(chat);
        var result = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(code, result.ErrorCode);
    }
}

public sealed class WireRedactionReviewFinalTests
{
    [Fact]
    public async Task F02_loopback_image_content_must_not_resend_the_key_in_request_body()
    {
        const string key = "review-final-key-QX42";
        using var server = new FakeChatServer().EnqueueJson(200, """
        {"id":"chatcmpl-image","object":"chat.completion","created":1700000000,"model":"fake-model","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":[{"type":"text","text":"answer"},{"type":"image_url","image_url":{"url":"https://example.com/review-final-key-QX42"}}]}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
        """).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", key, "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { key });
        var first = await t.Engine.RunTurnAsync("one").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TurnStatuses.Completed, first.Status);
        var second = await t.Engine.RunTurnAsync("two").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TurnStatuses.Completed, second.Status);
        Assert.Equal(2, server.Requests.Count);
        Assert.DoesNotContain(key, server.Requests[1].Body);
    }
}

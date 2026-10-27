using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.AI;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Agent.ReviewFixes;

public sealed class ReviewS4Controls
{
    [Fact]
    public async Task C01_Multiple_assistant_messages_answer_every_call_before_followup()
    {
        var chat = new ScriptedChatClient().Enqueue((_, _) => Task.FromResult(new ChatResponse(new[]
        {
            CallsResponse(("c1", "inspect_scene", "{}")).Messages[0],
            CallsResponse(("c2", "inspect_scene", "{}")).Messages[0],
        }))).EnqueueText("done");
        await using var t = await EngineAsync(chat);
        var result = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(TurnStatuses.Completed, result.Status);
        Assert.Equal(new[] { "c1", "c2" }, result.ToolCalls.Select(c => c.CallId));
        Assert.Equal(new[] { "c1", "c2" }, ResultsIn(chat.Requests[1]).Select(c => c.CallId));
    }

    [Fact]
    public async Task C02_Nonblank_unknown_wire_name_reaches_pipeline()
    {
        using var server = new FakeChatServer().EnqueueToolCalls(("c1", "tool.with.dot", "{}")).EnqueueText("done");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", "fake-review-key", "test-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client);
        var result = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(TurnStatuses.Completed, result.Status);
        Assert.Equal("unknown_tool", Assert.Single(result.ToolCalls).ErrorCode);
        Assert.Equal("c1", Assert.Single(result.ToolCalls).CallId);
    }

    [Fact]
    public async Task C03_Four_rounds_execute_last_batch_without_sending_fifth_request()
    {
        var chat = new ScriptedChatClient();
        for (var i = 1; i <= 5; i++) chat.EnqueueFunctionCalls(($"c{i}", "inspect_scene", "{}"));
        await using var t = await EngineAsync(chat);
        var result = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(TurnStatuses.BudgetExhausted, result.Status);
        Assert.Equal(4, chat.RequestCount);
        Assert.Equal(4, result.Rounds);
        Assert.Equal(4, result.ToolCalls.Count);
        Assert.Equal(1, chat.Remaining);
        chat.EnqueueText("next turn");
        await t.Engine.RunTurnAsync("continue");
        var answered = ResultsIn(chat.Requests[4]).Select(r => r.CallId).ToHashSet();
        foreach (var call in CallsIn(chat.Requests[4])) Assert.Contains(call.CallId, answered);
    }

    [Fact]
    public async Task C04_Redirect_to_another_endpoint_does_not_forward_authorization()
    {
        const string fakeKey = "fake-review-key";
        using var destination = new FakeChatServer().EnqueueText("done");
        using var redirect = new RedirectEndpoint(destination.BaseUrl + "/chat/completions");
        using var client = ChatClientFactory.Create(new LlmSettings("openai_compatible", fakeKey, "test-model", redirect.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { fakeKey });
        await t.Engine.RunTurnAsync("hello");
        Assert.Equal("Bearer " + fakeKey, await redirect.Authorization.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.All(destination.Requests, request => Assert.Null(request.Authorization));
        Assert.All(destination.Requests, request => Assert.DoesNotContain(fakeKey, request.Body));
    }

    private sealed class RedirectEndpoint : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;
        private readonly TaskCompletionSource<string?> _authorization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string BaseUrl { get; }
        public Task<string?> Authorization => _authorization.Task;

        public RedirectEndpoint(string location)
        {
            var reserve = new TcpListener(IPAddress.Loopback, 0);
            reserve.Start();
            var port = ((IPEndPoint)reserve.LocalEndpoint).Port;
            reserve.Stop();
            BaseUrl = $"http://127.0.0.1:{port}/v1";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _loop = Task.Run(async () =>
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    using var reader = new StreamReader(context.Request.InputStream);
                    await reader.ReadToEndAsync();
                    _authorization.TrySetResult(context.Request.Headers["Authorization"]);
                    context.Response.StatusCode = 307;
                    context.Response.RedirectLocation = location;
                    context.Response.Close();
                }
                catch (Exception ex) { _authorization.TrySetException(ex); }
            });
        }

        public void Dispose()
        {
            _listener.Close();
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
    }
}

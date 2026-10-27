using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sensei.Engine.Tests.Agent;

/// <summary>One request the fake server received.</summary>
public sealed record FakeRequest(string Method, string Path, string? Authorization, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();
}

/// <summary>
/// A loopback OpenAI-compatible chat-completions endpoint for tests (127.0.0.1 only, no external network). Replies come from
/// a queue; with an empty queue it answers with an empty assistant message.
/// </summary>
public sealed class FakeChatServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly object _sync = new();
    private readonly Queue<(int Status, string Body, TimeSpan Delay)> _replies = new();
    private readonly List<FakeRequest> _requests = new();
    private readonly Task _loop;

    public FakeChatServer()
    {
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}/v1";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _loop = Task.Run(Loop);
    }

    /// <summary>Base URL to give <c>LlmSettings</c>; http is allowed for 127.0.0.1 (S79).</summary>
    public string BaseUrl { get; }

    public IReadOnlyList<FakeRequest> Requests
    {
        get { lock (_sync) return _requests.ToList(); }
    }

    public FakeChatServer EnqueueText(string content, int promptTokens = 11, int completionTokens = 7) =>
        EnqueueJson(200, Completion(new JsonObject { ["role"] = "assistant", ["content"] = content }, "stop", promptTokens, completionTokens));

    /// <summary>One assistant message with tool calls; <c>arguments</c> is the raw string the model sent (may be invalid JSON).</summary>
    public FakeChatServer EnqueueToolCalls(params (string Id, string Name, string Arguments)[] calls)
    {
        var array = new JsonArray();
        foreach (var c in calls)
            array.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
            });
        return EnqueueJson(200, Completion(new JsonObject { ["role"] = "assistant", ["content"] = null, ["tool_calls"] = array }, "tool_calls", 12, 9));
    }

    public FakeChatServer EnqueueStatus(int status, string body = "{\"error\":{\"message\":\"provider said no\"}}") => EnqueueJson(status, body);

    public FakeChatServer EnqueueJson(int status, string body, TimeSpan? delay = null)
    {
        lock (_sync) _replies.Enqueue((status, body, delay ?? TimeSpan.Zero));
        return this;
    }

    public void Dispose()
    {
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }

    private static string Completion(JsonObject message, string finish, int prompt, int completion) =>
        new JsonObject
        {
            ["id"] = "chatcmpl-test",
            ["object"] = "chat.completion",
            ["created"] = 1_700_000_000,
            ["model"] = "test-model",
            ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["message"] = message, ["finish_reason"] = finish }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = prompt, ["completion_tokens"] = completion, ["total_tokens"] = prompt + completion },
        }.ToJsonString();

    private async Task Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }
            _ = Task.Run(() => Handle(context));
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync();
            (int Status, string Body, TimeSpan Delay) reply;
            lock (_sync)
            {
                _requests.Add(new FakeRequest(context.Request.HttpMethod, context.Request.Url!.AbsolutePath, context.Request.Headers["Authorization"], body));
                reply = _replies.Count > 0 ? _replies.Dequeue() : (200, Completion(new JsonObject { ["role"] = "assistant", ["content"] = "" }, "stop", 1, 1), TimeSpan.Zero);
            }
            if (reply.Delay > TimeSpan.Zero) await Task.Delay(reply.Delay);
            var bytes = Encoding.UTF8.GetBytes(reply.Body);
            context.Response.StatusCode = reply.Status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
        catch (Exception)
        {
            try
            {
                context.Response.Abort();
            }
            catch (Exception)
            {
            }
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

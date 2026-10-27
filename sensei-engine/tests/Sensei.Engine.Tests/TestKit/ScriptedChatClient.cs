using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Sensei.Engine.Tests.TestKit;

/// <summary>
/// An <see cref="IChatClient"/> that answers from a queue of scripted responses (text, function calls, errors, delays)
/// and records every request. No network. When the queue is empty it answers with an empty assistant message.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly object _sync = new();
    private readonly Queue<Func<RecordedChatRequest, CancellationToken, Task<ChatResponse>>> _script = new();
    private readonly List<RecordedChatRequest> _requests = new();

    public IReadOnlyList<RecordedChatRequest> Requests
    {
        get { lock (_sync) return _requests.ToList(); }
    }

    public int RequestCount
    {
        get { lock (_sync) return _requests.Count; }
    }

    public int Remaining
    {
        get { lock (_sync) return _script.Count; }
    }

    /// <summary>Queues a plain assistant text answer.</summary>
    public ScriptedChatClient EnqueueText(string text, long inputTokens = 10, long outputTokens = 5) =>
        Enqueue((_, _) => Task.FromResult(WithUsage(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)), inputTokens, outputTokens)));

    /// <summary>Queues one assistant message carrying function calls, in order: (call id, tool name, arguments JSON).</summary>
    public ScriptedChatClient EnqueueFunctionCalls(params (string CallId, string Name, string ArgumentsJson)[] calls)
    {
        var contents = calls.Select(c => (AIContent)new FunctionCallContent(c.CallId, c.Name, ParseArguments(c.ArgumentsJson))).ToList();
        return Enqueue((_, _) => Task.FromResult(WithUsage(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)), 10, 5)));
    }

    /// <summary>Queues an exception thrown by the provider call.</summary>
    public ScriptedChatClient EnqueueError(Exception error) => Enqueue((_, _) => Task.FromException<ChatResponse>(error));

    /// <summary>Queues a response that waits (honouring cancellation) before answering with <paramref name="then"/>.</summary>
    public ScriptedChatClient EnqueueDelayed(TimeSpan delay, ChatResponse then) => Enqueue(async (_, ct) =>
    {
        await Task.Delay(delay, ct);
        return then;
    });

    /// <summary>Queues an arbitrary responder.</summary>
    public ScriptedChatClient Enqueue(Func<RecordedChatRequest, CancellationToken, Task<ChatResponse>> responder)
    {
        lock (_sync) _script.Enqueue(responder);
        return this;
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = new RecordedChatRequest(messages.Select(m => m.Clone()).ToList(), options?.Clone());
        Func<RecordedChatRequest, CancellationToken, Task<ChatResponse>>? next;
        lock (_sync)
        {
            _requests.Add(request);
            next = _script.Count > 0 ? _script.Dequeue() : null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (next is null) return new ChatResponse(new ChatMessage(ChatRole.Assistant, ""));
        return await next(request, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates()) yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    /// <summary>Parses a JSON object into the dictionary shape <see cref="FunctionCallContent"/> carries (values are <see cref="JsonElement"/>).</summary>
    public static Dictionary<string, object?> ParseArguments(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
    }

    private static ChatResponse WithUsage(ChatResponse response, long input, long output)
    {
        response.Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output };
        return response;
    }
}

/// <summary>One request the scripted client received.</summary>
public sealed record RecordedChatRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

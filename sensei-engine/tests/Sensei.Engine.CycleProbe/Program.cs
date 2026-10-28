using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Sensei.Engine;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Storage;
using Sensei.Engine.Trace;

// A finite two-object back-reference. No large or deeply nested input is constructed.
var holder = new Holder();
JsonNode value = args.Contains("acyclic") ? JsonValue.Create(new { Text = "hello" })! : JsonValue.Create(holder)!;
holder.Node = value;
var trace = new Trace();
var chat = new SeededChat(value);
var worker = new Thread(() =>
{
    try
    {
        Run().GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        Console.WriteLine("Unexpected catchable error: " + ex.GetType().Name);
        Environment.ExitCode = 4;
    }
}, 1_048_576);
worker.Start();
worker.Join();

async Task Run()
{
    await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
    {
        ChatClient = chat, ProfileStore = new Store(), Speech = new Speech(), Trace = trace,
    });
    var one = await engine.RunTurnAsync("one");
    if (one.Status != TurnStatuses.Completed) throw new InvalidOperationException("seed failed");
    var two = await engine.RunTurnAsync("two");
    Console.WriteLine($"status={two.Status}; detail={two.ErrorDetail}; rejections={trace.Rejections}; requests={chat.Requests}");
    Environment.ExitCode = args.Contains("acyclic")
        ? (two.Status == TurnStatuses.Completed && chat.Requests == 2 ? 0 : 3)
        : (two.Status == TurnStatuses.Failed && two.ErrorDetail == "outbound request rejected: not_serializable"
           && trace.Rejections == 1 && chat.Requests == 1 ? 0 : 2);
}

public sealed class Holder { public JsonNode? Node { get; set; } }
sealed class Trace : ITraceSink
{
    public int Rejections;
    public void Write(TraceEntry e) { if (e.ErrorCode == "outbound_rejected") Rejections++; }
}
sealed class Store : IProfileStore
{
    public Task<LearnerProfile?> LoadAsync(string id, CancellationToken ct) => Task.FromResult<LearnerProfile?>(null);
    public Task SaveAsync(LearnerProfile p, CancellationToken ct) => Task.CompletedTask;
    public Task DeleteAsync(string id, CancellationToken ct) => Task.CompletedTask;
}
sealed class Speech : ISpeechOutput
{
    public Task<SpeechOutcome> SpeakAsync(SpeechRequest r, CancellationToken ct) => Task.FromResult(new SpeechOutcome("completed", null));
}
sealed class SeededChat(JsonNode node) : IChatClient
{
    public int Requests;
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests++;
        return Task.FromResult(new ChatResponse(Requests == 1
            ? new List<ChatMessage> { new(ChatRole.Assistant, "seed"), new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("prior", node) }) }
            : new List<ChatMessage> { new(ChatRole.Assistant, "done") }));
    }
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates()) yield return update;
    }
    public object? GetService(Type t, object? key = null) => null;
    public void Dispose() { }
}

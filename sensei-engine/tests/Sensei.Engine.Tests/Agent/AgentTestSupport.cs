using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Speech;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Tests.Agent;

/// <summary>Helpers for agent-loop tests: engines wired to a scripted chat client and readers for what the model was sent.</summary>
internal static class AgentTestSupport
{
    public static Task<TestEngine> EngineAsync(ScriptedChatClient chat, bool probes = true, RecordingSpeechOutput? speech = null,
        RecordingEvents? events = null, RecordingTraceSink? trace = null, InMemoryProfileStore? store = null,
        IReadOnlyList<string>? knownSecrets = null, params SenseiTool[] more) =>
        TestEngine.CreateAsync(chat: chat, speech: speech, events: events, trace: trace, store: store, knownSecrets: knownSecrets,
            extraTools: (probes ? new SenseiTool[] { new ProbeTool(), new MutatingProbeTool() } : Array.Empty<SenseiTool>()).Concat(more).ToArray());

    /// <summary>The text of every message with the given role in a request, joined.</summary>
    public static string TextOf(RecordedChatRequest request, ChatRole role) =>
        string.Join("\n", request.Messages.Where(m => m.Role == role).Select(m => m.Text));

    /// <summary>All text a request carries: instructions plus every message.</summary>
    public static string Everything(RecordedChatRequest request) =>
        (request.Options?.Instructions ?? "") + "\n" + string.Join("\n", request.Messages.Select(m => m.Text));

    public static IReadOnlyList<ChatMessage> UserMessages(RecordedChatRequest request) =>
        request.Messages.Where(m => m.Role == ChatRole.User).ToList();

    /// <summary>The last user message of a request: the current turn's learner block.</summary>
    public static string LearnerBlock(RecordedChatRequest request) => UserMessages(request).Last().Text;

    /// <summary>All function results found in a request, in message order.</summary>
    public static IReadOnlyList<FunctionResultContent> ResultsIn(RecordedChatRequest request) =>
        request.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();

    /// <summary>All function calls found in a request, in message order.</summary>
    public static IReadOnlyList<FunctionCallContent> CallsIn(RecordedChatRequest request) =>
        request.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();

    /// <summary>The engine result JSON of a function result.</summary>
    public static JsonElement ResultJson(FunctionResultContent result) =>
        result.Result is JsonElement element ? element : JsonDocument.Parse(result.Result?.ToString() ?? "null").RootElement.Clone();

    public static string ErrorCodeOf(FunctionResultContent result)
    {
        var json = ResultJson(result);
        return json.TryGetProperty("error_code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString()! : "";
    }

    public static string DetailOf(FunctionResultContent result)
    {
        var json = ResultJson(result);
        return json.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String ? detail.GetString()! : "";
    }

    public static string StatusOf(FunctionResultContent result) => ResultJson(result).GetProperty("status").GetString()!;

    /// <summary>A response carrying one assistant message with function calls.</summary>
    public static ChatResponse CallsResponse(params (string CallId, string Name, string Json)[] calls) =>
        new(new ChatMessage(ChatRole.Assistant, calls.Select(c => (AIContent)new FunctionCallContent(c.CallId, c.Name, ScriptedChatClient.ParseArguments(c.Json))).ToList()))
        {
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 },
        };

    /// <summary>Waits until <paramref name="condition"/> holds (polling), failing the test after <paramref name="timeout"/>.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var limit = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("condition not reached in time");
            await Task.Delay(5);
        }
    }

    public static string ProbeJson(string action, string? text = null, int? delayMs = null, string? idsJson = null)
    {
        var parts = new List<string> { $"\"action\":\"{action}\"" };
        if (text is not null) parts.Add($"\"text\":\"{text}\"");
        if (delayMs is not null) parts.Add($"\"delay_ms\":{delayMs}");
        if (idsJson is not null) parts.Add($"\"ids\":{idsJson}");
        return "{" + string.Join(",", parts) + "}";
    }
}

/// <summary>A probe tool that records when each call ran, to show calls of one response never overlap (S61).</summary>
internal sealed class TimedProbeTool : ProbeTool
{
    private readonly object _sync = new();

    public TimedProbeTool() : base("test_timed")
    {
    }

    public List<(string Text, DateTime Start, DateTime End)> Spans { get; } = new();

    public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        await Task.Delay(60, ct);
        var end = DateTime.UtcNow;
        lock (_sync) Spans.Add((args.Text ?? "", start, end));
        return context.Success();
    }
}

/// <summary>A probe tool that stores a speech-history entry for the turn, standing in for the speech tools of another slice.</summary>
internal sealed class SpeechProbeTool : ProbeTool
{
    public SpeechProbeTool() : base("test_say")
    {
    }

    public override Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
    {
        string id;
        lock (context.Sync)
        {
            id = context.NextSpeechUtteranceId();
            context.SpeechHistory.Add(new SpeechHistoryEntry
            {
                UtteranceId = id,
                Segments = new List<SpeechSegment> { new("ja", args.Text ?? "はい", null, null, null) },
                Pace = "normal",
                TurnId = context.TurnId,
                GenerationId = context.GenerationId,
                SegmentsPresented = 1,
                Outcome = "success",
            });
        }
        return Task.FromResult(context.Success());
    }
}

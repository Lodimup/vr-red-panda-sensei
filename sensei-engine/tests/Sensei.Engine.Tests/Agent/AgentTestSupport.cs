using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Agent;

/// <summary>Helpers for agent-loop tests: engines wired to a scripted chat client and readers for what the model was sent.</summary>
internal static class AgentTestSupport
{
    public static Task<TestEngine> EngineAsync(ScriptedChatClient chat, bool probes = true, RecordingSpeechOutput? speech = null,
        RecordingEvents? events = null, RecordingTraceSink? trace = null, InMemoryProfileStore? store = null,
        IReadOnlyList<string>? knownSecrets = null) =>
        TestEngine.CreateAsync(chat: chat, speech: speech, events: events, trace: trace, store: store, knownSecrets: knownSecrets,
            extraTools: probes ? new Sensei.Engine.Tools.SenseiTool[] { new ProbeTool(), new MutatingProbeTool() } : null);

    /// <summary>The text of every message with the given role in a request, joined.</summary>
    public static string TextOf(RecordedChatRequest request, ChatRole role) =>
        string.Join("\n", request.Messages.Where(m => m.Role == role).Select(m => m.Text));

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

    public static string StatusOf(FunctionResultContent result) => ResultJson(result).GetProperty("status").GetString()!;
}

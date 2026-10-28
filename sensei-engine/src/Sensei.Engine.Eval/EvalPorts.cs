using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Tasks;
using Sensei.Engine.Trace;
using Sensei.Engine.World;

namespace Sensei.Engine.Eval;

/// <summary>The console of one conversation: every line goes to a buffer and, when asked, to the real console at once.</summary>
internal sealed class ConversationLog
{
    private readonly StringBuilder _buffer = new();
    private readonly bool _live;
    private readonly object _gate = new();

    public ConversationLog(bool live)
    {
        _live = live;
    }

    public void Line(string text)
    {
        lock (_gate)
        {
            _buffer.Append(text).Append('\n');
            if (_live)
            {
                Console.Out.WriteLine(text);
                Console.Out.Flush();
            }
        }
    }

    public override string ToString()
    {
        lock (_gate) return _buffer.ToString();
    }
}

/// <summary>What is measured during one turn, filled by the ports while the turn runs.</summary>
internal sealed class TurnProbe
{
    public Stopwatch Clock { get; } = Stopwatch.StartNew();
    public long? FirstSpeakMs { get; set; }
    public int Utterances { get; set; }
    public List<(string Lang, string Text)> SpokenSegments { get; } = new();
}

/// <summary>
/// The speech port of the evaluation: prints each segment the way the CLI does (S70, S91) and records when the first
/// utterance reached the learner and every line that was spoken. Speech is instant; the evaluation measures the model.
/// </summary>
internal sealed class EvalSpeech : ISpeechOutput
{
    private readonly ConversationLog _log;
    private TurnProbe? _probe;

    public EvalSpeech(ConversationLog log)
    {
        _log = log;
    }

    public void Begin(TurnProbe probe) => _probe = probe;

    public Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct)
    {
        var probe = _probe;
        if (probe is not null)
        {
            probe.FirstSpeakMs ??= probe.Clock.ElapsedMilliseconds;
            probe.Utterances++;
        }
        var captions = request.EffectiveCaptions;
        foreach (var segment in request.Segments)
        {
            probe?.SpokenSegments.Add((segment.Lang, segment.Text));
            _log.Line(Format(segment, captions));
        }
        return Task.FromResult(new SpeechOutcome(SpeechStatuses.Completed, null, request.Segments.Count));
    }

    private static string Format(SpeechSegment segment, SpeechCaptions captions)
    {
        if (segment.Lang != SpeechLanguages.Japanese) return $"Sensei [{segment.Lang}]: {segment.Text}";
        var line = $"Sensei [ja]: {(captions.ShowJapanese ? segment.Text : "(audio)")}";
        if (captions.ShowReading && !string.IsNullOrEmpty(segment.Reading)) line += $" | reading: {segment.Reading}";
        if (captions.ShowRomaji && !string.IsNullOrEmpty(segment.Romaji)) line += $" | romaji: {segment.Romaji}";
        if (captions.ShowTranslation && !string.IsNullOrEmpty(segment.Translation)) line += $" | translation: {segment.Translation}";
        return line;
    }
}

/// <summary>Writes every trace entry to a JSON-lines file and echoes the CLI's <c>[tool]</c> line to the conversation log (S72, S73).</summary>
internal sealed class EvalTrace : ITraceSink
{
    private readonly string _path;
    private readonly ConversationLog _log;
    private readonly object _gate = new();

    public EvalTrace(string path, ConversationLog log)
    {
        _path = path;
        _log = log;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Write(TraceEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, EvalJson.Compact);
        lock (_gate) File.AppendAllText(_path, line + "\n");
        _log.Line($"[tool] {entry.Tool} {entry.Status}{(string.IsNullOrEmpty(entry.ErrorCode) ? "" : " " + entry.ErrorCode)} rev={entry.Revision.ToString(CultureInfo.InvariantCulture)}");
    }
}

/// <summary>Prints the hint cards the learner would see.</summary>
internal sealed class EvalEvents : IEngineEvents
{
    private readonly ConversationLog _log;

    public EvalEvents(ConversationLog log)
    {
        _log = log;
    }

    public void OnHint(HintCard card)
    {
        var fields = new[] { card.Japanese, card.Reading, card.Romaji, card.English }.Where(f => !string.IsNullOrEmpty(f));
        _log.Line($"Card [{card.Kind}]: {string.Join(" / ", fields)}");
    }

    public void OnSceneChanged(SceneEvent e, SceneSnapshot scene) { }
    public void OnHighlight(IReadOnlyList<string> ids, int durationMs) { }
    public void OnAnimate(string animation, string? targetId) { }
    public void OnSupportChanged(SupportSettings settings) { }
    public void OnTaskChanged(TaskView task) { }
    public void OnSummary(SessionSummary summary) { }
    public void OnSenseiState(string state) { }
}

/// <summary>One request that really went to the provider.</summary>
internal sealed record ModelRequest(long Ms, long? InputTokens, long? OutputTokens, long? ReasoningTokens, double? CostUsd);

/// <summary>Times every provider request of a conversation. It sits directly above the provider client.</summary>
internal sealed class TimingChatClient : DelegatingChatClient
{
    private readonly object _gate = new();
    private readonly List<ModelRequest> _requests = new();
    private readonly ConversationLog? _log;

    public TimingChatClient(IChatClient inner, ConversationLog? log = null) : base(inner)
    {
        _log = log;
    }

    /// <summary>The requests since the last call.</summary>
    public IReadOnlyList<ModelRequest> Drain()
    {
        lock (_gate)
        {
            var copy = _requests.ToArray();
            _requests.Clear();
            return copy;
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            // The provider's own words about a rejected request (never the request, which carries the key's owner's text only).
            var body = ex.GetRawResponse()?.Content?.ToString() ?? "";
            _log?.Line($"[model-error] HTTP {ex.Status} after {clock.ElapsedMilliseconds} ms: {(body.Length > 700 ? body[..700] : body)}");
            throw;
        }
        clock.Stop();
        if (_log is not null)
            foreach (var call in response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>())
                _log.Line($"[model-call-id] {call.Name} id shape {Shape(call.CallId)}");
        var (reasoning, cost) = ReadUsage(response);
        lock (_gate) _requests.Add(new ModelRequest(clock.ElapsedMilliseconds, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, reasoning, cost));
        return response;
    }

    /// <summary>The length and character classes of an id (letters a, digits 9, others kept): enough to see which redaction rule it meets.</summary>
    private static string Shape(string? id) =>
        id is null ? "null" : id.Length + ":" + new string(id.Select(c => char.IsLetter(c) ? 'a' : char.IsDigit(c) ? '9' : c).ToArray());

#pragma warning disable SCME0001 // JsonPatch: the provider's usage.cost is not a field of the OpenAI SDK's usage type.
    /// <summary>Reasoning tokens and the provider-reported cost (USD) of one response, when it carries them.</summary>
    private static (long? Reasoning, double? Cost) ReadUsage(ChatResponse response)
    {
        if (response.RawRepresentation is not OpenAI.Chat.ChatCompletion completion) return (null, null);
        var usage = completion.Usage;
        long? reasoning = usage?.OutputTokenDetails?.ReasoningTokenCount;
        double? cost = null;
        if (usage is not null && usage.Patch.TryGetValue("$.cost"u8, out double fromUsage)) cost = fromUsage;
        else if (completion.Patch.TryGetValue("$.usage.cost"u8, out double fromCompletion)) cost = fromCompletion;
        return (reasoning, cost);
    }
#pragma warning restore SCME0001

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("the agent loop does not stream");
}

internal static class EvalJson
{
    public static JsonSerializerOptions Compact { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

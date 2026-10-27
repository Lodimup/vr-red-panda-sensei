using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Sensei.Engine.Llm;
using Sensei.Engine.Storage;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Eval;

/// <summary>Everything measured about one turn.</summary>
internal sealed class TurnRecord
{
    public int Turn { get; set; }
    public string Learner { get; set; } = "";
    public bool JapaneseAttempt { get; set; }
    public long WallMs { get; set; }
    public long? FirstSpeakMs { get; set; }
    public int Rounds { get; set; }
    public int ToolCalls { get; set; }
    public List<string> Tools { get; set; } = new();
    public string Status { get; set; } = "";
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public int SpeakCalls { get; set; }
    public int Utterances { get; set; }
    public List<string> Spoken { get; set; } = new();
    public int DuplicateSpokenLines { get; set; }

    /// <summary>Spoken segments that are almost a line already spoken this turn (bigram Dice >= 0.8, at least 8 characters). Information only; the target counts exact repeats.</summary>
    public int NearDuplicateSpokenLines { get; set; }
    public int RecordAttemptCalls { get; set; }
    public int RecordAttemptSuccess { get; set; }
    public int FailedCalls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long? ReasoningTokens { get; set; }

    /// <summary>USD the provider reported for the turn (sum of usage.cost); null when it reports none.</summary>
    public double? CostUsd { get; set; }

    /// <summary>USD at the model's list price from the tokens of the turn; null when the price is unknown.</summary>
    public double? EstimatedCostUsd { get; set; }

    public List<ModelRequest> ModelRequests { get; set; } = new();
    public string? UnspokenText { get; set; }
}

/// <summary>What the learner model holds at the end of a conversation.</summary>
internal sealed class ConversationEnd
{
    public string? StartingBand { get; set; }
    public string WorkingLevel { get; set; } = "";
    public JsonNode? Labels { get; set; }
    public List<EvidenceLine> Evidence { get; set; } = new();
}

internal sealed class EvidenceLine
{
    public string AttemptId { get; set; } = "";
    public long TurnId { get; set; }
    public string Dimension { get; set; } = "";
    public string Key { get; set; } = "";
    public string? TranscriptExcerpt { get; set; }
    public string LanguageAssessment { get; set; } = "";
    public bool CommunicativeSuccess { get; set; }
    public string Outcome { get; set; } = "";
    public string EngineOutcome { get; set; } = "";
    public List<string> Assistance { get; set; } = new();
    public bool Superseded { get; set; }
}

internal sealed class ConversationRecord
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Band { get; set; }
    public List<TurnRecord> Turns { get; set; } = new();
    public ConversationEnd? End { get; set; }
}

/// <summary>Plays one scripted conversation against the real provider through the real engine and measures every turn.</summary>
internal static class ConversationRunner
{
    public static async Task<ConversationRecord> RunAsync(Conversation conversation, LlmSettings settings, string apiKey, string outDir, bool live, ListPrice? price, Action<ConversationLog> onDone, CancellationToken ct)
    {
        var log = new ConversationLog(live);
        var record = new ConversationRecord { Id = conversation.Id, Description = conversation.Description, Band = conversation.Band };

        var provider = new TimingChatClient(ChatClientFactory.Create(settings));
        var speech = new EvalSpeech(log);
        var trace = new EvalTrace(Path.Combine(outDir, "traces", conversation.Id + ".jsonl"), log);
        var store = new FileProfileStore(Path.Combine(outDir, "profiles", conversation.Id));
        await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
        {
            ChatClient = provider,
            Speech = speech,
            ProfileStore = store,
            ProfileId = conversation.Id,
            Events = new EvalEvents(log),
            Trace = trace,
            KnownSecrets = new[] { apiKey },
        }, ct).ConfigureAwait(false);

        log.Line($"=== conversation {conversation.Id}: {conversation.Description}");
        if (conversation.Band is { } band)
        {
            await engine.SetStartingBandAsync(band, ct).ConfigureAwait(false);
            log.Line($"(player picked starting band: {band})");
        }

        var number = 0;
        foreach (var turn in conversation.Turns)
        {
            number++;
            log.Line($"You{(turn.JapaneseAttempt ? " [japanese attempt]" : "")}: {turn.Text}");
            var probe = new TurnProbe();
            speech.Begin(probe);
            var result = await engine.RunTurnAsync(turn.Text, ct).ConfigureAwait(false);
            probe.Clock.Stop();
            var requests = provider.Drain();

            var row = Measure(number, turn, probe, result, requests, price);
            record.Turns.Add(row);
            if (!string.IsNullOrEmpty(result.UnspokenText)) log.Line("[unspoken] " + Clip(result.UnspokenText, 400));
            if (result.ErrorCode is not null) log.Line($"error: {result.ErrorCode}: {Clip(result.ErrorDetail ?? "", 300)}");
            log.Line("[model] " + string.Join(", ", requests.Select(r => $"{r.Ms} ms in={r.InputTokens} out={r.OutputTokens} reasoning={r.ReasoningTokens} cost={r.CostUsd?.ToString("0.000000", CultureInfo.InvariantCulture)}")));
            var first = row.FirstSpeakMs is { } f ? $"first speak {f} ms" : "no speech";
            log.Line($"Sensei: {Describe(result.Status)} (turn {result.TurnId}: {result.Status}, {result.Rounds} rounds, {result.ToolCalls.Count} tools, {row.WallMs} ms; {first}; speak calls {row.SpeakCalls}; duplicate lines {row.DuplicateSpokenLines} (near {row.NearDuplicateSpokenLines}); record_attempt {row.RecordAttemptSuccess}/{row.RecordAttemptCalls}; cost {FormatCost(row)})");
            if (result.Status == TurnStatuses.Failed && result.ErrorCode is TurnErrorCodes.AuthFailed or TurnErrorCodes.QuotaExceeded) break;
        }

        record.End = await EndStateAsync(engine, log, ct).ConfigureAwait(false);
        onDone(log);
        return record;
    }

    private static TurnRecord Measure(int number, ScriptTurn turn, TurnProbe probe, TurnResult result, IReadOnlyList<ModelRequest> requests, ListPrice? price)
    {
        var row = new TurnRecord
        {
            Turn = number,
            Learner = turn.Text,
            JapaneseAttempt = turn.JapaneseAttempt,
            WallMs = probe.Clock.ElapsedMilliseconds,
            FirstSpeakMs = probe.FirstSpeakMs,
            Rounds = result.Rounds,
            ToolCalls = result.ToolCalls.Count,
            Status = result.Status,
            ErrorCode = result.ErrorCode,
            ErrorDetail = result.ErrorDetail,
            Utterances = probe.Utterances,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            ModelRequests = requests.ToList(),
            UnspokenText = result.UnspokenText,
            ReasoningTokens = requests.Any(r => r.ReasoningTokens is not null) ? requests.Sum(r => r.ReasoningTokens ?? 0) : null,
            CostUsd = requests.Any(r => r.CostUsd is not null) ? requests.Sum(r => r.CostUsd ?? 0) : null,
            EstimatedCostUsd = price is null ? null : requests.Sum(r => (r.InputTokens ?? 0) * price.Prompt + (r.OutputTokens ?? 0) * price.Completion),
        };
        foreach (var call in result.ToolCalls.Where(c => !c.Duplicate))
        {
            row.Tools.Add($"{call.Tool}:{call.Status}{(string.IsNullOrEmpty(call.ErrorCode) ? "" : ":" + call.ErrorCode)}");
            if (call.Status != ToolStatuses.Success) row.FailedCalls++;
            if (call.Tool == "speak") row.SpeakCalls++;
            if (call.Tool == "record_attempt")
            {
                row.RecordAttemptCalls++;
                if (call.Status == ToolStatuses.Success) row.RecordAttemptSuccess++;
            }
        }
        row.Spoken = probe.SpokenSegments.Select(s => $"[{s.Lang}] {s.Text}").ToList();
        row.DuplicateSpokenLines = CountDuplicates(probe.SpokenSegments.Select(s => s.Lang + "|" + SpokenLines.Normalize(s.Text)));
        row.NearDuplicateSpokenLines = SpokenLines.CountNearDuplicates(probe.SpokenSegments.Select(s => (s.Lang, SpokenLines.Normalize(s.Text))).ToList());
        return row;
    }

    /// <summary>Spoken segments whose normalized text was already spoken earlier in the same turn.</summary>
    private static int CountDuplicates(IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        foreach (var key in keys)
        {
            var text = key[(key.IndexOf('|') + 1)..];
            if (text.Length == 0) continue;
            if (!seen.Add(key)) duplicates++;
        }
        return duplicates;
    }

    private static async Task<ConversationEnd> EndStateAsync(SenseiEngine engine, ConversationLog log, CancellationToken ct)
    {
        var profile = engine.GetProfile();
        var end = new ConversationEnd { StartingBand = profile.StartingBand, WorkingLevel = profile.WorkingLevel };
        foreach (var e in profile.Evidence)
        {
            end.Evidence.Add(new EvidenceLine
            {
                AttemptId = e.AttemptId,
                TurnId = e.TurnId,
                Dimension = e.Target.Dimension,
                Key = e.Target.Key,
                TranscriptExcerpt = e.TranscriptExcerpt,
                LanguageAssessment = e.LanguageAssessment,
                CommunicativeSuccess = e.CommunicativeSuccess,
                Outcome = e.Outcome,
                EngineOutcome = e.EngineOutcome,
                Assistance = e.Assistance,
                Superseded = e.Superseded,
            });
        }

        // The labels come from the engine itself (query_learner, S53) in a synthetic manual turn after the conversation.
        engine.BeginTurn("(cli)");
        var labels = await engine.InvokeToolAsync("query_learner", "{}", null, ct).ConfigureAwait(false);
        if (labels.Data is { } data && data.TryGetProperty("targets", out var targets)) end.Labels = JsonNode.Parse(targets.GetRawText());

        log.Line("--- end of conversation");
        log.Line($"starting band: {end.StartingBand ?? "none"}; working level: {end.WorkingLevel} (hidden from learner)");
        log.Line("labels: " + (end.Labels is JsonArray { Count: > 0 } arr ? string.Join("; ", arr.Select(t => $"{t?["key"]} ({t?["dimension"]}): {t?["label"]}")) : "none"));
        foreach (var line in end.Evidence)
            log.Line($"evidence {line.AttemptId} turn {line.TurnId} {line.Dimension}/{line.Key}: \"{line.TranscriptExcerpt}\" {line.LanguageAssessment}, success={line.CommunicativeSuccess}, outcome={line.Outcome}");
        return end;
    }

    private static string FormatCost(TurnRecord row) =>
        row.CostUsd is { } reported ? $"${reported.ToString("0.00000", CultureInfo.InvariantCulture)}"
        : row.EstimatedCostUsd is { } estimate ? $"~${estimate.ToString("0.00000", CultureInfo.InvariantCulture)} (list price)"
        : "unknown";

    private static string Describe(string status) => status switch
    {
        TurnStatuses.Completed => "done",
        TurnStatuses.BudgetExhausted => "out of budget",
        TurnStatuses.Cancelled => "interrupted",
        _ => "failed",
    };

    private static string Clip(string text, int max)
    {
        var flat = text.Replace('\n', ' ');
        return flat.Length <= max ? flat : flat[..max] + " ...";
    }
}

/// <summary>Normalization that makes two spellings of the same spoken line equal.</summary>
internal static class SpokenLines
{
    /// <summary>Segments (later in the turn) that are not equal but almost equal to an earlier segment of the same language.</summary>
    public static int CountNearDuplicates(IReadOnlyList<(string Lang, string Text)> segments)
    {
        var count = 0;
        for (var i = 1; i < segments.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                var a = segments[j];
                var b = segments[i];
                if (a.Lang != b.Lang || a.Text == b.Text || a.Text.Length < 8 || b.Text.Length < 8) continue;
                if (Dice(a.Text, b.Text) >= 0.8)
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }

    private static double Dice(string a, string b)
    {
        var left = Bigrams(a);
        var right = Bigrams(b);
        var shared = 0;
        foreach (var pair in left)
            if (right.TryGetValue(pair.Key, out var other)) shared += Math.Min(pair.Value, other);
        return 2.0 * shared / (left.Values.Sum() + right.Values.Sum());
    }

    private static Dictionary<string, int> Bigrams(string text)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < text.Length; i++)
        {
            var key = text.Substring(i, 2);
            map[key] = map.TryGetValue(key, out var n) ? n + 1 : 1;
        }
        return map;
    }

    /// <summary>NFKC, lower case, and only letters and digits kept: punctuation, spacing and a trailing "!" do not make a line new.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            var category = char.GetUnicodeCategory(c);
            if (char.IsLetterOrDigit(c) || category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark) sb.Append(c);
        }
        return sb.ToString();
    }
}

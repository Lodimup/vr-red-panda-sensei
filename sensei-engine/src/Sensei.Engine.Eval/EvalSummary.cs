namespace Sensei.Engine.Eval;

internal sealed class Target
{
    public string Name { get; set; } = "";
    public string Rule { get; set; } = "";
    public double Value { get; set; }
    public bool Pass { get; set; }
}

internal sealed class EvalSummary
{
    public int Turns { get; set; }
    public int FailedTurns { get; set; }
    public long MedianWallMs { get; set; }
    public long P95WallMs { get; set; }
    public long MaxWallMs { get; set; }
    public long? MedianFirstSpeakMs { get; set; }
    public long? P95FirstSpeakMs { get; set; }
    public int TurnsWithoutSpeech { get; set; }
    public int BudgetExhaustedTurns { get; set; }
    public int TurnsWithDuplicateLines { get; set; }
    public int DuplicateLinesTotal { get; set; }
    public int AttemptTurns { get; set; }
    public int AttemptTurnsWithRecordAttempt { get; set; }
    public double RecordAttemptRate { get; set; }
    public int TotalRecordAttemptCalls { get; set; }
    public int FailedToolCalls { get; set; }
    public double MeanRounds { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long? ReasoningTokens { get; set; }
    public long InputTokensMedianPerRequest { get; set; }
    public long InputTokensMaxPerRequest { get; set; }

    /// <summary>Total USD of the run: the provider-reported usage.cost when every turn has it, else the list-price estimate.</summary>
    public double? TotalCostUsd { get; set; }

    public double? MeanTurnCostUsd { get; set; }

    /// <summary><c>provider_usage_cost</c> or <c>list_price_estimate</c>.</summary>
    public string CostSource { get; set; } = "unknown";

    public string PercentileMethod { get; set; } = "nearest_rank";
    public List<Target> Targets { get; set; } = new();
    public bool AllMeasuredTargetsPass { get; set; }
    public string ManualCheck { get; set; } = "no turn where Sensei ignores a learner question to push the task: read the transcript";

    public static EvalSummary Of(IReadOnlyList<ConversationRecord> conversations)
    {
        var turns = conversations.SelectMany(c => c.Turns).ToList();
        var wall = turns.Select(t => t.WallMs).OrderBy(x => x).ToList();
        var first = turns.Where(t => t.FirstSpeakMs is not null).Select(t => t.FirstSpeakMs!.Value).OrderBy(x => x).ToList();
        var attempts = turns.Where(t => t.JapaneseAttempt).ToList();
        var s = new EvalSummary
        {
            Turns = turns.Count,
            FailedTurns = turns.Count(t => t.Status == TurnStatuses.Failed),
            MedianWallMs = wall.Count == 0 ? 0 : Median(wall),
            P95WallMs = wall.Count == 0 ? 0 : Percentile(wall, 0.95),
            MaxWallMs = wall.Count == 0 ? 0 : wall[^1],
            MedianFirstSpeakMs = first.Count == 0 ? null : Median(first),
            P95FirstSpeakMs = first.Count == 0 ? null : Percentile(first, 0.95),
            TurnsWithoutSpeech = turns.Count(t => t.FirstSpeakMs is null),
            BudgetExhaustedTurns = turns.Count(t => t.Status == TurnStatuses.BudgetExhausted),
            TurnsWithDuplicateLines = turns.Count(t => t.DuplicateSpokenLines > 0),
            DuplicateLinesTotal = turns.Sum(t => t.DuplicateSpokenLines),
            AttemptTurns = attempts.Count,
            AttemptTurnsWithRecordAttempt = attempts.Count(t => t.RecordAttemptCalls > 0),
            TotalRecordAttemptCalls = turns.Sum(t => t.RecordAttemptCalls),
            FailedToolCalls = turns.Sum(t => t.FailedCalls),
            MeanRounds = turns.Count == 0 ? 0 : Math.Round(turns.Average(t => t.Rounds), 2),
            InputTokens = turns.Sum(t => t.InputTokens),
            OutputTokens = turns.Sum(t => t.OutputTokens),
        };
        var inputs = turns.SelectMany(t => t.ModelRequests).Select(r => r.InputTokens ?? 0).OrderBy(x => x).ToList();
        s.InputTokensMedianPerRequest = inputs.Count == 0 ? 0 : Median(inputs);
        s.InputTokensMaxPerRequest = inputs.Count == 0 ? 0 : inputs[^1];
        s.ReasoningTokens = turns.Any(t => t.ReasoningTokens is not null) ? turns.Sum(t => t.ReasoningTokens ?? 0) : null;
        if (turns.Count > 0 && turns.All(t => t.CostUsd is not null))
        {
            s.TotalCostUsd = turns.Sum(t => t.CostUsd!.Value);
            s.CostSource = "provider_usage_cost";
        }
        else if (turns.Count > 0 && turns.All(t => t.EstimatedCostUsd is not null))
        {
            s.TotalCostUsd = turns.Sum(t => t.EstimatedCostUsd!.Value);
            s.CostSource = "list_price_estimate";
        }
        if (s.TotalCostUsd is { } total && turns.Count > 0) s.MeanTurnCostUsd = total / turns.Count;
        s.RecordAttemptRate = s.AttemptTurns == 0 ? 0 : Math.Round((double)s.AttemptTurnsWithRecordAttempt / s.AttemptTurns, 3);
        s.Targets.Add(new Target { Name = "median_wall_ms", Rule = "<= 12000", Value = s.MedianWallMs, Pass = s.MedianWallMs <= 12000 });
        s.Targets.Add(new Target { Name = "p95_wall_ms", Rule = "<= 25000", Value = s.P95WallMs, Pass = s.P95WallMs <= 25000 });
        s.Targets.Add(new Target { Name = "budget_exhausted_turns", Rule = "<= 1", Value = s.BudgetExhaustedTurns, Pass = s.BudgetExhaustedTurns <= 1 });
        s.Targets.Add(new Target { Name = "turns_with_duplicate_lines", Rule = "== 0", Value = s.TurnsWithDuplicateLines, Pass = s.TurnsWithDuplicateLines == 0 });
        s.Targets.Add(new Target { Name = "record_attempt_rate", Rule = ">= 0.8", Value = s.RecordAttemptRate, Pass = s.AttemptTurns > 0 && s.RecordAttemptRate >= 0.8 });
        s.Targets.Add(new Target { Name = "failed_turns", Rule = "== 0 (provider errors void the run)", Value = s.FailedTurns, Pass = s.FailedTurns == 0 });
        s.AllMeasuredTargetsPass = s.Targets.All(t => t.Pass);
        return s;
    }

    private static long Median(IReadOnlyList<long> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    /// <summary>Nearest rank: the smallest value with at least p of the values at or below it.</summary>
    private static long Percentile(IReadOnlyList<long> sorted, double p)
    {
        var rank = (int)Math.Ceiling(p * sorted.Count);
        return sorted[Math.Max(0, Math.Min(sorted.Count - 1, rank - 1))];
    }
}

internal sealed class RunInfo
{
    public string StartedAt { get; set; } = "";
    public string FinishedAt { get; set; } = "";
    public string Label { get; set; } = "";
    public string GitSha { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ReasoningEffort { get; set; } = "";
    public int MaxOutputTokens { get; set; }
    public int Parallel { get; set; }
    public string Scripts { get; set; } = "";
    public double? ListPricePromptPerMtok { get; set; }
    public double? ListPriceCompletionPerMtok { get; set; }

    /// <summary>Growth of the key's own spend counter over the run (USD). It also counts anyone else using the key.</summary>
    public double? KeySpendDeltaUsd { get; set; }
}

internal sealed class EvalOutput
{
    public RunInfo Run { get; set; } = new();
    public EvalSummary Summary { get; set; } = new();
    public List<ConversationRecord> Conversations { get; set; } = new();
}

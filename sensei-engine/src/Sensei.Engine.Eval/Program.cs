using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Sensei.Engine.Eval;
using Sensei.Engine.Llm;

// sensei-eval: plays the fixed scripted conversations against the real provider through the real engine and writes
// live-eval.json (see sensei-engine/scripts/live-eval.sh). Not part of `dotnet test`.
Console.OutputEncoding = new UTF8Encoding(false);

const string Usage = "usage: sensei-eval --scripts <file> --out <dir> [--only <id,id>] [--parallel <n>] [--label <text>] [--git-sha <sha>]\n" +
                     "  needs SENSEI_LLM_API_KEY (and optionally the other SENSEI_LLM_* variables) in the environment.";

string? scriptsPath = null, outDir = null, only = null, label = "", gitSha = "";
var parallel = 1;
for (var i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    try
    {
        switch (args[i])
        {
            case "--scripts": scriptsPath = Next(); break;
            case "--out": outDir = Next(); break;
            case "--only": only = Next(); break;
            case "--label": label = Next(); break;
            case "--git-sha": gitSha = Next(); break;
            case "--parallel": parallel = int.Parse(Next(), CultureInfo.InvariantCulture); break;
            default: throw new ArgumentException($"unknown option {args[i]}");
        }
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException)
    {
        Console.Error.WriteLine("error: " + ex.Message);
        Console.Error.WriteLine(Usage);
        return 1;
    }
}
if (scriptsPath is null || outDir is null || parallel < 1)
{
    Console.Error.WriteLine(Usage);
    return 1;
}

var env = new Dictionary<string, string?>(StringComparer.Ordinal);
foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) env[(string)entry.Key] = entry.Value as string;

LlmSettings settings;
try
{
    settings = LlmSettings.FromEnvironment(env);
}
catch (LlmConfigurationException ex)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 1;
}
var apiKey = env[LlmSettings.ApiKeyVariable]!.Trim();

IReadOnlyList<Conversation> conversations;
try
{
    conversations = EvalScripts.Load(scriptsPath);
}
catch (Exception ex) when (ex is FormatException or IOException)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 1;
}
if (only is not null)
{
    var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    conversations = conversations.Where(c => wanted.Contains(c.Id)).ToList();
    if (conversations.Count == 0)
    {
        Console.Error.WriteLine("error: --only matches no conversation");
        return 1;
    }
}

Directory.CreateDirectory(outDir);
var run = new RunInfo
{
    StartedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    Label = label ?? "",
    GitSha = gitSha ?? "",
    Provider = settings.Provider,
    Model = settings.Model,
    BaseUrl = settings.BaseUrl,
    ReasoningEffort = settings.ReasoningEffort,
    MaxOutputTokens = settings.MaxOutputTokens,
    Parallel = parallel,
    Scripts = Path.GetFileName(scriptsPath),
};
Console.WriteLine($"live-eval: {conversations.Count} conversations, {conversations.Sum(c => c.Turns.Count)} learner turns, {settings}");
if (!string.IsNullOrEmpty(label)) Console.WriteLine($"live-eval: label '{label}', code {gitSha}");

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

var price = settings.Provider == LlmSettings.OpenRouter ? await ProviderCost.ListPriceAsync(settings.BaseUrl, settings.Model, cancel.Token).ConfigureAwait(false) : null;
var spendBefore = settings.Provider == LlmSettings.OpenRouter ? await ProviderCost.KeySpendAsync(settings.BaseUrl, apiKey, cancel.Token).ConfigureAwait(false) : null;
run.ListPricePromptPerMtok = price is null ? null : price.Prompt * 1_000_000;
run.ListPriceCompletionPerMtok = price is null ? null : price.Completion * 1_000_000;

var records = new ConversationRecord[conversations.Count];
var printed = new ConversationLog?[conversations.Count];
var gate = new SemaphoreSlim(parallel);
var live = parallel == 1;
var tasks = conversations.Select(async (conversation, index) =>
{
    await gate.WaitAsync(cancel.Token).ConfigureAwait(false);
    try
    {
        records[index] = await ConversationRunner.RunAsync(conversation, settings, apiKey, outDir, live, price, log => printed[index] = log, cancel.Token).ConfigureAwait(false);
    }
    finally
    {
        gate.Release();
    }
}).ToArray();
try
{
    await Task.WhenAll(tasks).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("live-eval: cancelled");
    return 130;
}

if (!live)
{
    foreach (var log in printed) Console.Write(log!.ToString());
}

var spendAfter = settings.Provider == LlmSettings.OpenRouter ? await ProviderCost.KeySpendAsync(settings.BaseUrl, apiKey, CancellationToken.None).ConfigureAwait(false) : null;
if (spendBefore is { } before && spendAfter is { } after) run.KeySpendDeltaUsd = after - before;
run.FinishedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
var output = new EvalOutput { Run = run, Conversations = records.ToList(), Summary = EvalSummary.Of(records) };
var jsonPath = Path.Combine(outDir, "live-eval.json");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(output, EvalJson.Options), new UTF8Encoding(false));

var s = output.Summary;
Console.WriteLine();
Console.WriteLine("=== summary");
Console.WriteLine($"turns {s.Turns}; failed {s.FailedTurns}; wall median {s.MedianWallMs} ms, p95 {s.P95WallMs} ms, max {s.MaxWallMs} ms; first speak median {s.MedianFirstSpeakMs} ms, p95 {s.P95FirstSpeakMs} ms; mean rounds {s.MeanRounds}");
Console.WriteLine($"budget_exhausted {s.BudgetExhaustedTurns}; turns with duplicate lines {s.TurnsWithDuplicateLines} ({s.DuplicateLinesTotal} lines; {s.TurnsWithNearDuplicateLines} turns with near-duplicates); record_attempt on {s.AttemptTurnsWithRecordAttempt}/{s.AttemptTurns} attempt turns ({s.RecordAttemptRate:P0}); failed tool calls {s.FailedToolCalls}; tokens in {s.InputTokens} out {s.OutputTokens}");
Console.WriteLine($"input tokens per request: median {s.InputTokensMedianPerRequest}, max {s.InputTokensMaxPerRequest}; reasoning tokens {s.ReasoningTokens?.ToString(CultureInfo.InvariantCulture) ?? "not reported"}");
Console.WriteLine($"cost: {(s.TotalCostUsd is { } total ? "$" + total.ToString("0.0000", CultureInfo.InvariantCulture) : "unknown")} for the run, {(s.MeanTurnCostUsd is { } mean ? "$" + mean.ToString("0.00000", CultureInfo.InvariantCulture) : "unknown")} per turn ({s.CostSource}); key spend counter grew by {(run.KeySpendDeltaUsd is { } delta ? "$" + delta.ToString("0.0000", CultureInfo.InvariantCulture) : "unknown")} (includes anyone else using the key)");
foreach (var t in s.Targets) Console.WriteLine($"  {(t.Pass ? "PASS" : "FAIL")} {t.Name} {t.Rule}: {t.Value.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"  MANUAL {s.ManualCheck}");
Console.WriteLine($"live-eval: wrote {jsonPath}");
if (s.FailedTurns > 0) return 2;
return s.AllMeasuredTargetsPass ? 0 : 3;

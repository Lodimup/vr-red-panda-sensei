using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Llm;
using Sensei.Engine.Storage;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using Sensei.Engine.World;

namespace Sensei.Engine.Cli;

/// <summary>
/// The command-line front end of the engine (SPEC §13): learner lines run turns, slash commands drive the scene, the
/// profile and single tools. In <c>--script</c> mode every line runs to completion before the next and the exit code
/// reports what happened (S74, S110); interactively, input is read while a turn runs (S109).
/// </summary>
internal sealed class CliApp
{
    private static CliApp? _active;

    private readonly CliOptions _options;
    private readonly CliOutput _output;
    private readonly Secrets _secrets;
    private readonly string? _configError;
    private SenseiEngine _engine = null!;
    private CliTraceSink _trace = null!;
    private CancellationTokenSource _quit = new();
    private readonly List<Task> _turnTasks = new();
    private int _runningCount;
    private bool _manualTurnOpen;
    private bool _usageError;
    private bool _llmError;

    private CliApp(CliOptions options, CliOutput output, Secrets secrets, string? configError)
    {
        _options = options;
        _output = output;
        _secrets = secrets;
        _configError = configError;
    }

    /// <summary>Runs the CLI. Returns the process exit code (0, 1 or 2 in script mode, S74/S110).</summary>
    /// <param name="chatClient">Test seam: use this client instead of building one from the environment.</param>
    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, IReadOnlyDictionary<string, string?> env,
        CancellationToken quit = default, IChatClient? chatClient = null)
    {
        // S77: the configured key is known before anything is printed, so even an option error or a configuration error is cleaned.
        var knownSecrets = new List<string>();
        if (env.TryGetValue(LlmSettings.ApiKeyVariable, out var rawKey) && !string.IsNullOrWhiteSpace(rawKey)) knownSecrets.Add(rawKey.Trim());
        var secrets = new Secrets(knownSecrets);
        var writer = new CliOutput(output, secrets);
        var options = CliOptions.Parse(args, out var optionError);
        if (options is null)
        {
            writer.Line($"error: {optionError}");
            writer.Line(CliOptions.Usage);
            return 1;
        }
        if (options.Help)
        {
            writer.Line(CliOptions.Usage);
            return 0;
        }

        // Settings: a missing or invalid configuration does not stop the CLI; utterances report it (S74).
        LlmSettings? settings = null;
        string? configError = null;
        try
        {
            settings = LlmSettings.FromEnvironment(env);
        }
        catch (LlmConfigurationException ex)
        {
            configError = ex.Message;
        }

        var app = new CliApp(options, writer, secrets, configError);
        app._quit = CancellationTokenSource.CreateLinkedTokenSource(quit);
        return await app.RunCoreAsync(input, chatClient ?? (settings is null ? null : ChatClientFactory.Create(settings)), knownSecrets).ConfigureAwait(false);
    }

    /// <summary>Ctrl+C: cancels the running turn; returns false when no turn runs (the caller then quits).</summary>
    public static bool TryCancelRunningTurn()
    {
        var app = _active;
        if (app is null || Volatile.Read(ref app._runningCount) == 0) return false;
        app._engine.CancelGeneration();
        return true;
    }

    private async Task<int> RunCoreAsync(TextReader input, IChatClient? chatClient, List<string> knownSecrets)
    {
        _trace = new CliTraceSink(_options.ProfileDir, _output, _options.Trace);
        var voice = _options.Voice == "say" ? new SayVoice() : null;
        try
        {
            _engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
            {
                ChatClient = chatClient,
                Speech = new CliSpeechOutput(_output, voice),
                ProfileStore = new FileProfileStore(_options.ProfileDir),
                ProfileId = _options.ProfileId,
                Events = new CliEvents(_output),
                Trace = _trace,
                KnownSecrets = knownSecrets,
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever the engine's boundary hands back for a start that failed (a storage error arrives as SenseiEngineException,
            // S77) is a start-up error: it is reported on one line and the exit code is 1 (S110). Never an unhandled exception.
            _output.Line("error: " + Clean(ex.Message));
            return 1;
        }

        _active = this;
        try
        {
            if (_options.ScriptPath is { } script) return await RunScriptAsync(script).ConfigureAwait(false);
            return await RunInteractiveAsync(input).ConfigureAwait(false);
        }
        finally
        {
            _active = null;
            await _engine.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- modes

    private async Task<int> RunScriptAsync(string path)
    {
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _output.Line("error: cannot read script: " + Clean(ex.Message));
            return 1;
        }

        foreach (var line in lines)
        {
            if (_quit.IsCancellationRequested) break;
            if (await HandleLineAsync(line, wait: true).ConfigureAwait(false)) break;
        }
        await DrainAsync().ConfigureAwait(false);
        return _usageError ? 1 : _llmError ? 2 : 0;
    }

    private async Task<int> RunInteractiveAsync(TextReader input)
    {
        _output.Line($"Red Panda Sensei - session {_engine.SessionId}. Type /help for commands, /quit to leave.");
        while (!_quit.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await input.ReadLineAsync(_quit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (line is null)
            {
                // End of input: let the turns that are still running finish, then leave.
                await DrainAsync().ConfigureAwait(false);
                return 0;
            }
            if (await HandleLineAsync(line, wait: false).ConfigureAwait(false)) break;
        }
        _engine.CancelGeneration();
        await DrainAsync().ConfigureAwait(false);
        return 0;
    }

    // ---------------------------------------------------------------- lines and commands

    /// <summary>Handles one input line. Returns true when the session should end (<c>/quit</c>).</summary>
    private async Task<bool> HandleLineAsync(string line, bool wait)
    {
        var text = line.Trim();
        if (text.Length == 0) return false;
        try
        {
            if (!text.StartsWith('/'))
            {
                await StartTurnAsync(ct => _engine.RunTurnAsync(text, ct), wait).ConfigureAwait(false);
                return false;
            }

            var space = text.IndexOfAny(new[] { ' ', '\t' });
            var command = (space < 0 ? text : text[..space]).ToLowerInvariant();
            var rest = space < 0 ? "" : text[(space + 1)..].Trim();
            switch (command)
            {
                case "/help":
                    _output.Line(HelpText);
                    break;
                case "/scene":
                    PrintScene();
                    break;
                case "/select":
                    Select(rest);
                    break;
                case "/move":
                    Move(rest);
                    break;
                case "/tool":
                    await ToolAsync(rest).ConfigureAwait(false);
                    break;
                case "/band":
                    await BandAsync(rest).ConfigureAwait(false);
                    break;
                case "/pref":
                    await PreferenceAsync(rest).ConfigureAwait(false);
                    break;
                case "/profile":
                    PrintProfile();
                    break;
                case "/summary":
                    _output.Line("summary: " + JsonSerializer.Serialize(_engine.GetSessionSummary(), SenseiJsonContext.Default.SessionSummary));
                    break;
                case "/correct":
                    if (rest.Length == 0) Fail("usage: /correct <text>");
                    else await StartTurnAsync(ct => _engine.CorrectLastTranscriptAsync(rest, ct), wait).ConfigureAwait(false);
                    break;
                case "/cancel":
                    _engine.CancelGeneration();
                    _manualTurnOpen = false;
                    _output.Line("cancelled");
                    break;
                case "/reset":
                    await _engine.ResetProfileAsync().ConfigureAwait(false);
                    _manualTurnOpen = false;
                    _output.Line("profile reset");
                    break;
                case "/quit":
                case "/exit":
                    return true;
                default:
                    Fail($"unknown command '{command}' (try /help)");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The session is ending.
        }
        catch (Exception ex)
        {
            Fail("unhandled exception: " + ex.GetType().Name + ": " + ex.Message);
        }
        return false;
    }

    private const string HelpText =
        "/help                         this list\n" +
        "/scene                        show the scene\n" +
        "/select <id>                  select an object as the player\n" +
        "/move <id> <region|container> move an object as the player\n" +
        "/tool <name> <json>           call one tool as Sensei\n" +
        "/band <band>                  set the starting band\n" +
        "/pref <field> <value>         set a preference (it is pinned)\n" +
        "/profile                      show the learner profile\n" +
        "/summary                      show the session summary\n" +
        "/correct <text>               correct the last transcript\n" +
        "/cancel                       cancel the running turn\n" +
        "/reset                        reset the profile\n" +
        "/quit                         leave";

    /// <summary>Waits until every started turn has printed its result.</summary>
    private async Task DrainAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_turnTasks)
            {
                _turnTasks.RemoveAll(t => t.IsCompleted);
                pending = _turnTasks.ToArray();
            }
            if (pending.Length == 0) return;
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    /// <summary>Starts a turn; in script mode waits for it, interactively lets it run while input continues (S109).</summary>
    private async Task StartTurnAsync(Func<CancellationToken, Task<TurnResult>> run, bool wait)
    {
        _manualTurnOpen = false;
        Interlocked.Increment(ref _runningCount);
        var task = RunAndReportAsync(run);
        lock (_turnTasks) _turnTasks.Add(task);
        if (wait) await task.ConfigureAwait(false);
    }

    private async Task RunAndReportAsync(Func<CancellationToken, Task<TurnResult>> run)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            TurnResult result;
            try
            {
                result = await run(_quit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Fail("unhandled exception: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }
            watch.Stop();
            Report(result, watch.ElapsedMilliseconds);
        }
        finally
        {
            Interlocked.Decrement(ref _runningCount);
        }
    }

    /// <summary>Prints a finished turn: errors, trace-only text and the S75 result line.</summary>
    private void Report(TurnResult result, long elapsedMs)
    {
        if (_options.Trace && !string.IsNullOrWhiteSpace(result.UnspokenText)) _output.Line("[unspoken] " + result.UnspokenText!.Replace('\n', ' '));

        switch (result.ErrorCode)
        {
            case null:
                break;
            case TurnErrorCodes.LlmNotConfigured:
                _llmError = true;
                _output.Line("error: " + (_configError ?? LlmSettings.ApiKeyVariable + " is not set"));
                break;
            case TurnErrorCodes.NothingToCorrect:
                // S110 lists exactly what exits 1 and what exits 2; a correction with nothing to correct is neither, so it is reported and exits 0.
                _output.Line($"error: {result.ErrorCode}: {Clean(result.ErrorDetail)}");
                break;
            default:
                _llmError = true;
                _output.Line($"error: {result.ErrorCode}: {Clean(result.ErrorDetail)}");
                break;
        }

        var verdict = result.Status switch
        {
            TurnStatuses.Completed => "done",
            TurnStatuses.Cancelled => "interrupted",
            TurnStatuses.BudgetExhausted => "out of budget",
            _ => "failed",
        };
        _output.Line($"Sensei: {verdict} (turn {result.TurnId}: {result.Status}, {result.Rounds} rounds, {result.ToolCalls.Count} tools, {elapsedMs} ms)");
    }

    private void Fail(string message)
    {
        _usageError = true;
        _output.Line("error: " + Clean(message));
    }

    private string Clean(string? text) => (_secrets.Sanitize(text) ?? "").Replace('\r', ' ').Replace('\n', ' ');

    // ---------------------------------------------------------------- commands

    private void PrintScene()
    {
        var scene = _engine.GetScene();
        _output.Line($"Scene rev {scene.Revision}, selected: {scene.SelectedId ?? "none"}");
        if (scene.Instances.Count == 0) _output.Line("(the room is empty)");
        else
        {
            _output.Line("id | catalog | region | slot | in | japanese | reading | english");
            foreach (var i in scene.Instances)
                _output.Line($"{i.Id} | {i.CatalogId} | {i.Region} | {i.Slot} | {i.ContainerId ?? "-"} | {i.Japanese} | {i.Reading} | {i.English}");
        }
        if (scene.ActiveTask is { } task)
        {
            var instruction = task.Instruction.English ?? task.Instruction.Japanese ?? "";
            _output.Line($"Task {task.TaskId} [{task.Family}] {task.Status}: {instruction}");
        }
    }

    private void Select(string rest)
    {
        if (rest.Length == 0)
        {
            Fail("usage: /select <id>");
            return;
        }
        var result = _engine.PlayerSelect(rest);
        if (result.Ok) _output.Line($"selected {rest} (rev {result.Revision})");
        else ActionRefused(result);
    }

    private void Move(string rest)
    {
        var parts = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            Fail("usage: /move <id> <region|container_id>");
            return;
        }
        var destination = parts[1];
        var result = SceneRegions.IsRegion(destination)
            ? _engine.PlayerMove(parts[0], destination, null)
            : _engine.PlayerMove(parts[0], null, destination);
        if (result.Ok) _output.Line($"moved {parts[0]} to {destination} (rev {result.Revision})");
        else ActionRefused(result);
    }

    /// <summary>A refused player action. Bad arguments count as command errors; an engine refusal is a normal outcome.</summary>
    private void ActionRefused(PlayerActionResult result)
    {
        var line = $"error: {result.ErrorCode}: {Clean(result.Detail)}";
        if (result.ErrorCode == ToolErrorCodes.InvalidArguments || result.ErrorCode == ToolErrorCodes.UnknownObject) _usageError = true;
        _output.Line(line);
    }

    private async Task ToolAsync(string rest)
    {
        var space = rest.IndexOfAny(new[] { ' ', '\t' });
        var name = space < 0 ? rest : rest[..space];
        var json = space < 0 ? "" : rest[(space + 1)..].Trim();
        // S71: the command is `/tool <name> <json>`; a call without its JSON is a malformed command (S110: exit 1), not a tool failure.
        if (name.Length == 0 || json.Length == 0)
        {
            Fail("usage: /tool <name> <json>");
            return;
        }

        if (!_manualTurnOpen)
        {
            try
            {
                _engine.BeginTurn("(cli)");
            }
            catch (InvalidOperationException ex)
            {
                Fail(ex.Message);
                return;
            }
            _manualTurnOpen = true;
        }

        _trace.SuppressEcho = true;
        ToolResult result;
        try
        {
            result = await _engine.InvokeToolAsync(name, json).ConfigureAwait(false);
        }
        finally
        {
            _trace.SuppressEcho = false;
        }

        _output.Line(CliTraceSink.ToolLine(result.Tool, result.Status, result.ErrorCode, result.Revision));
        // S72: `data` is printed as one line of JSON, and nothing else is on that line.
        if (result.Data is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } data) _output.Line(data.GetRawText());
        if (result.Status != ToolStatuses.Success && !string.IsNullOrEmpty(result.Detail)) _output.Line("detail: " + Clean(result.Detail));
        if (result.Status == ToolStatuses.Cancelled) _manualTurnOpen = false;
    }

    private async Task BandAsync(string rest)
    {
        if (rest.Length == 0)
        {
            Fail($"usage: /band <{string.Join("|", StartingBands.All)}>");
            return;
        }
        try
        {
            await _engine.SetStartingBandAsync(rest).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            Fail(ex.Message);
            return;
        }
        var support = _engine.GetProfile().Support;
        _output.Line($"band set to {rest}: show_japanese={Bool(support.ShowJapanese)} show_reading={Bool(support.ShowReading)} show_romaji={Bool(support.ShowRomaji)} show_translation={Bool(support.ShowTranslation)} language_mode={support.LanguageMode}");
    }

    private async Task PreferenceAsync(string rest)
    {
        var parts = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            Fail("usage: /pref <field> <value>");
            return;
        }
        var (field, value) = (parts[0], parts[1]);
        var patch = new SupportPatch();
        switch (field)
        {
            case SupportFields.ShowJapanese:
            case SupportFields.ShowReading:
            case SupportFields.ShowRomaji:
            case SupportFields.ShowTranslation:
                if (!bool.TryParse(value, out var flag))
                {
                    Fail($"{field}: must be true or false");
                    return;
                }
                if (field == SupportFields.ShowJapanese) patch.ShowJapanese = flag;
                else if (field == SupportFields.ShowReading) patch.ShowReading = flag;
                else if (field == SupportFields.ShowRomaji) patch.ShowRomaji = flag;
                else patch.ShowTranslation = flag;
                break;
            case SupportFields.Pace:
                patch.Pace = value;
                break;
            case SupportFields.CorrectionStyle:
                patch.CorrectionStyle = value;
                break;
            case SupportFields.LanguageMode:
                patch.LanguageMode = value;
                break;
            default:
                Fail($"unknown preference '{field}'; allowed: {string.Join(", ", SupportFields.All)}");
                return;
        }
        try
        {
            await _engine.SetPreferenceByPlayerAsync(patch).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            Fail(ex.Message);
            return;
        }
        _output.Line($"preference {field}={value} (pinned)");
    }

    private void PrintProfile()
    {
        var profile = _engine.GetProfile();
        var support = profile.Support;
        _output.Line($"profile {profile.ProfileId} (session {_engine.SessionId})");
        _output.Line($"starting band: {profile.StartingBand ?? "not chosen"}");
        _output.Line($"working level: {profile.WorkingLevel} (hidden from learner)");
        _output.Line("support: " + string.Join(" ", SupportFields.All.Select(f => f + "=" + support.GetFieldValue(f))));
        _output.Line("pinned: " + (profile.Pinned.Count == 0 ? "none" : string.Join(", ", profile.Pinned)));
        var labels = EvidenceRules.ComputeLabels(profile.Evidence);
        _output.Line("labels: " + (labels.Count == 0 ? "none" : string.Join(", ", labels.Select(l => $"{l.Key} ({l.Dimension}): {l.Label}"))));
        _output.Line("notes: " + (profile.Notes.Count == 0 ? "none" : string.Join("; ", profile.Notes.OrderBy(n => n.Key, StringComparer.Ordinal).Select(n => $"{n.Key}={n.Value}"))));
    }

    private static string Bool(bool value) => value ? "true" : "false";
}

using System.Collections;
using System.Text;
using Sensei.Engine.Cli;
using Sensei.Engine.Trace;

// sensei: the command-line front end of the engine (SPEC §13).
Console.OutputEncoding = new UTF8Encoding(false);
Console.InputEncoding = new UTF8Encoding(false);

var env = new Dictionary<string, string?>(StringComparer.Ordinal);
foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
    env[(string)entry.Key] = entry.Value as string;

using var quit = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // First Ctrl+C cancels the running turn; with nothing running it ends the session.
    e.Cancel = true;
    if (!CliApp.TryCancelRunningTurn()) quit.Cancel();
};

try
{
    return await CliApp.RunAsync(args, Console.In, Console.Out, env, quit.Token);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // The last boundary (S110): nothing escapes as an unhandled exception. One cleaned line, exit 1.
    var key = env.TryGetValue("SENSEI_LLM_API_KEY", out var raw) && !string.IsNullOrWhiteSpace(raw) ? new[] { raw.Trim() } : Array.Empty<string>();
    var secrets = new Secrets(key);
    Console.Out.WriteLine("error: unhandled exception: " + ex.GetType().Name + ": " + (secrets.Sanitize(ex.Message) ?? "").Replace('\r', ' ').Replace('\n', ' '));
    Console.Out.Flush();
    return 1;
}

using System.Collections;
using System.Text;
using Sensei.Engine.Cli;

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

return await CliApp.RunAsync(args, Console.In, Console.Out, env, quit.Token);

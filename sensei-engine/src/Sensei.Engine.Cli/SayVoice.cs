using System.ComponentModel;
using System.Diagnostics;

namespace Sensei.Engine.Cli;

/// <summary>
/// Speaks through the macOS <c>say</c> program (S118: the program named <c>say</c> resolved through <c>PATH</c>, never an
/// absolute path). Kyoko for Japanese, Samantha for English; slow pace is speech rate 120.
/// </summary>
internal sealed class SayVoice
{
    public const string JapaneseVoice = "Kyoko";
    public const string EnglishVoice = "Samantha";
    public const int SlowRate = 120;

    /// <summary>The arguments of one <c>say</c> call.</summary>
    internal static IReadOnlyList<string> Arguments(string lang, string text, string pace)
    {
        var args = new List<string> { "-v", lang == "ja" ? JapaneseVoice : EnglishVoice };
        if (pace == "slow")
        {
            args.Add("-r");
            args.Add(SlowRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (text.StartsWith('-')) args.Add("--");
        args.Add(text);
        return args;
    }

    /// <summary>Runs <c>say</c> to completion. Returns null on success, <c>"cancelled"</c> when cancelled, else the failure text.</summary>
    public async Task<string?> SpeakAsync(string lang, string text, string pace, CancellationToken ct)
    {
        var info = new ProcessStartInfo("say")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var arg in Arguments(lang, text, pace)) info.ArgumentList.Add(arg);

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Win32Exception ex)
        {
            return "cannot run say: " + ex.Message;
        }
        if (process is null) return "cannot run say";

        using (process)
        {
            var errors = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return "cancelled";
            }
            if (process.ExitCode == 0) return null;
            var message = (await errors.ConfigureAwait(false)).Trim();
            return $"say exited with code {process.ExitCode}" + (message.Length == 0 ? "" : ": " + message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone.
        }
    }
}

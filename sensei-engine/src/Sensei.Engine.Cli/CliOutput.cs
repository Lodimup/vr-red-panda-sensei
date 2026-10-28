using Sensei.Engine.Trace;

namespace Sensei.Engine.Cli;

/// <summary>
/// The one writer all output goes through. S77: every string leaves through <see cref="Secrets.Sanitize"/> before anything else is
/// done to it, so a known secret or a credential-pattern match never reaches the console, whichever path printed it. A line is
/// written whole under a lock, so lines from a running turn, the engine's events and the commands never interleave mid-line (S109).
/// </summary>
internal sealed class CliOutput
{
    private readonly TextWriter _writer;
    private readonly Secrets _secrets;
    private readonly object _gate = new();

    public CliOutput(TextWriter writer, Secrets? secrets = null)
    {
        _writer = writer;
        _secrets = secrets ?? Secrets.None;
    }

    /// <summary>The secrets this writer removes; the trace sink uses the same set for the lines it writes to disk (D6).</summary>
    public Secrets Secrets => _secrets;

    public void Line(string text)
    {
        var clean = _secrets.Sanitize(text) ?? "";
        lock (_gate)
        {
            foreach (var line in clean.Replace("\r\n", "\n").Split('\n')) _writer.WriteLine(line);
            _writer.Flush();
        }
    }
}

namespace Sensei.Engine.Cli;

/// <summary>
/// The one writer all output goes through: a line is written whole under a lock, so lines from a running turn, the
/// engine's events and the commands never interleave mid-line (S109).
/// </summary>
internal sealed class CliOutput
{
    private readonly TextWriter _writer;
    private readonly object _gate = new();

    public CliOutput(TextWriter writer)
    {
        _writer = writer;
    }

    public void Line(string text)
    {
        lock (_gate)
        {
            foreach (var line in text.Replace("\r\n", "\n").Split('\n')) _writer.WriteLine(line);
            _writer.Flush();
        }
    }
}

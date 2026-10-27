using System.Text.Json;
using Sensei.Engine.Json;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Cli;

/// <summary>
/// S73: appends every trace entry as a JSON line to <c>&lt;profile-dir&gt;/traces/&lt;session_id&gt;.jsonl</c>; with
/// <c>--trace</c> it also echoes <c>[tool] &lt;name&gt; &lt;status&gt;[ &lt;error_code&gt;] rev=&lt;revision&gt;</c> (S72).
/// </summary>
internal sealed class CliTraceSink : ITraceSink
{
    private readonly string _directory;
    private readonly CliOutput _output;
    private readonly bool _echo;
    private readonly object _gate = new();

    public CliTraceSink(string profileDir, CliOutput output, bool echo)
    {
        _directory = Path.Combine(profileDir, "traces");
        _output = output;
        _echo = echo;
    }

    /// <summary>Set while a <c>/tool</c> call prints its own line, so the call is not echoed twice.</summary>
    public volatile bool SuppressEcho;

    public void Write(TraceEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, SenseiJsonContext.Default.TraceEntry);
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            File.AppendAllText(Path.Combine(_directory, entry.SessionId + ".jsonl"), line + "\n");
        }
        if (_echo && !SuppressEcho) _output.Line(ToolLine(entry.Tool, entry.Status, entry.ErrorCode, entry.Revision));
    }

    /// <summary>The S72 line.</summary>
    public static string ToolLine(string tool, string status, string? errorCode, long revision) =>
        $"[tool] {tool} {status}{(string.IsNullOrEmpty(errorCode) ? "" : " " + errorCode)} rev={revision}";
}

using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.TestKit;

/// <summary>Collects trace entries.</summary>
public sealed class RecordingTraceSink : ITraceSink
{
    private readonly object _sync = new();
    private readonly List<TraceEntry> _entries = new();

    public IReadOnlyList<TraceEntry> Entries
    {
        get { lock (_sync) return _entries.ToList(); }
    }

    public void Write(TraceEntry entry)
    {
        lock (_sync) _entries.Add(entry);
    }

    public void Clear()
    {
        lock (_sync) _entries.Clear();
    }
}

using System;
using System.Text.Json.Serialization;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Trace;

/// <summary>Receives one entry per tool call and per event-handler failure (S28, S86).</summary>
public interface ITraceSink
{
    void Write(TraceEntry entry);
}

/// <summary>Opt in to additional model-round timing entries on the existing trace sink.</summary>
public interface IRoundTraceSink : ITraceSink { }

/// <summary>One trace line (S28, S86). Every string is sanitized (S77).</summary>
public sealed class TraceEntry
{
    public DateTimeOffset At { get; set; }
    public string SessionId { get; set; } = "";
    public long TurnId { get; set; }
    public long GenerationId { get; set; }
    public int Round { get; set; }
    public string CallId { get; set; } = "";
    public string Tool { get; set; } = "";

    /// <summary>Sanitized JSON string of the arguments as received.</summary>
    public string Arguments { get; set; } = "";

    public string Status { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ErrorCode { get; set; }

    public long Revision { get; set; }
    public long ElapsedMs { get; set; }
    public bool Duplicate { get; set; }

    /// <summary>The sanitized result exactly as returned to the model (S86); null for event-handler failures.</summary>
    public ToolResult? Result { get; set; }
}

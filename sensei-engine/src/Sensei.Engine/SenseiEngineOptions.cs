using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Sensei.Engine.Events;
using Sensei.Engine.Speech;
using Sensei.Engine.Storage;
using Sensei.Engine.Trace;

namespace Sensei.Engine;

/// <summary>Everything the engine needs from its host (SPEC §3).</summary>
public sealed class SenseiEngineOptions
{
    /// <summary>null = tools usable, <c>RunTurnAsync</c> fails with <c>llm_not_configured</c>.</summary>
    public required IChatClient? ChatClient { get; init; }

    public required ISpeechOutput Speech { get; init; }
    public required IProfileStore ProfileStore { get; init; }
    public string ProfileId { get; init; } = "default";
    public IEngineEvents? Events { get; init; }
    public ITraceSink? Trace { get; init; }
    public TimeProvider? Clock { get; init; }

    /// <summary>Exact secret values (the CLI passes its API key) redacted everywhere and refused in persisted learner text (S77).</summary>
    public IReadOnlyList<string> KnownSecrets { get; init; } = Array.Empty<string>();
}

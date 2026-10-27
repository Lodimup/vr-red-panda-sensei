using System;

namespace Sensei.Engine;

/// <summary>
/// The one exception type a <see cref="SenseiEngine"/> member throws for a failure that is not a documented one
/// (a store, speech port, provider, tool body or cancellation callback failed, or the engine itself did). S77: the
/// <see cref="Exception.Message"/> is written by the engine and nothing else: <c>&lt;operation&gt; failed: &lt;category&gt;</c>, where
/// the category is one of <c>profile_store_error</c>, <c>speech_output_error</c>, <c>event_handler_error</c>, <c>llm_error</c>,
/// <c>internal_error</c> and <c>invalid_configuration</c>. No text the failing dependency produced is read to build it, and
/// <see cref="Exception.InnerException"/> is always null. The documented exceptions (<see cref="InvalidOperationException"/> for
/// <c>turn_in_progress</c>, <see cref="ArgumentException"/>, <see cref="Storage.ProfileCorruptException"/>,
/// <see cref="OperationCanceledException"/>) are re-created by the engine with engine-authored messages and no inner exception.
/// A bounded, sanitized description of the original goes to the trace.
/// </summary>
public sealed class SenseiEngineException : Exception
{
    internal SenseiEngineException(string originalType, string message)
        : base(message)
    {
        OriginalType = originalType;
    }

    /// <summary>Full name of the exception type that was replaced (sanitized), so the failure stays classifiable.</summary>
    public string OriginalType { get; }
}

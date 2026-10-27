using System;

namespace Sensei.Engine;

/// <summary>
/// The one exception type a <see cref="SenseiEngine"/> member throws for a failure that is not a documented one
/// (a store, speech port, provider, tool body or cancellation callback failed, or the engine itself did). S77: the
/// <see cref="Exception.Message"/> has known secrets and credential-pattern matches replaced by <c>[redacted]</c>, and
/// <see cref="Exception.InnerException"/> is always null, so nothing the failing dependency attached can reach the host.
/// The documented exceptions (<see cref="InvalidOperationException"/> for <c>turn_in_progress</c>, <see cref="ArgumentException"/>,
/// <see cref="Storage.ProfileCorruptException"/>, <see cref="OperationCanceledException"/>) are thrown as themselves with the
/// same sanitizing and no inner exception. The full sanitized failure is written to the trace.
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

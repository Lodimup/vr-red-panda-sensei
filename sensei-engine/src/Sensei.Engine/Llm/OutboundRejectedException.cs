using System;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Llm;

/// <summary>
/// D6: raised instead of sending a provider request whose bytes the outbound guard could not walk: the body is not one JSON document, or
/// it has more JSON values than <see cref="OutboundGuard.MaxValues"/>, or (for a host-supplied client) the messages could not be
/// serialized for the check. Nothing reaches the provider. The turn fails with <c>provider_error</c> and the trace records
/// <c>outbound_rejected</c>. The message is engine-authored from a closed set of reasons; it never holds request text.
/// </summary>
internal sealed class OutboundRejectedException : Exception
{
    /// <summary>The body is not a single valid JSON document.</summary>
    public const string NotJson = "not_json";

    /// <summary>The body has more JSON values than the guard walks.</summary>
    public const string TooManyValues = "too_many_values";

    /// <summary>The messages of a host-supplied client could not be serialized for the check.</summary>
    public const string NotSerializable = "not_serializable";

    public OutboundRejectedException(string reason)
        : base("outbound request rejected: " + reason)
    {
        Reason = reason;
    }

    /// <summary>One of <see cref="NotJson"/>, <see cref="TooManyValues"/>, <see cref="NotSerializable"/>.</summary>
    public string Reason { get; }

    internal static OutboundRejectedException For(JsonWalkStatus status) =>
        new(status == JsonWalkStatus.TooManyValues ? TooManyValues : NotJson);
}

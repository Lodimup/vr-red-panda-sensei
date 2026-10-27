using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Llm;

/// <summary>
/// D6: the last chokepoint before a provider request leaves, placed in the OpenAI client's pipeline just before the transport so it sees
/// exactly the bytes that are sent. It reads the request body, walks it as JSON with a forward-only reader (no recursion; at most
/// <see cref="MaxValues"/> values), sanitizes every string value and every property name after the reader has unescaped it (known secrets
/// per S126, credential pattern per S78), and sends the result. A body with nothing to clean is sent as it was; one that was cleaned is sent
/// re-serialized (same structure, same order, numbers as written). A body that is not one JSON document, or has more values than the cap,
/// is not sent: <see cref="OutboundRejectedException"/>. A string that itself holds JSON is only a string here, cleaned as text and never
/// parsed again. The Authorization header is not touched, and neither is any request without a body.
/// </summary>
internal sealed class OutboundGuard : PipelinePolicy
{
    /// <summary>The most JSON values one request body may hold; more and the request is not sent.</summary>
    internal const int MaxValues = JsonRedaction.MaxFinalValues;

    private readonly object _gate = new();
    private Secrets _secrets;

    public OutboundGuard(Secrets secrets)
    {
        _secrets = secrets;
    }

    /// <summary>From now on the guard also removes every secret <paramref name="other"/> knows. Called by an engine that uses the client.</summary>
    internal void Attach(Secrets other)
    {
        lock (_gate) _secrets = _secrets.Union(other);
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (message.Request.Content is { } content)
        {
            using var buffer = new MemoryStream();
            content.WriteTo(buffer, message.CancellationToken);
            Replace(message, content, buffer.ToArray());
        }
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (message.Request.Content is { } content)
        {
            using var buffer = new MemoryStream();
            await content.WriteToAsync(buffer, message.CancellationToken).ConfigureAwait(false);
            Replace(message, content, buffer.ToArray());
        }
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private void Replace(PipelineMessage message, BinaryContent original, byte[] body)
    {
        var final = Guarded(body);
        message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(final));
        original.Dispose();
    }

    /// <summary>The bytes that may be sent for <paramref name="body"/>; throws <see cref="OutboundRejectedException"/> if none may.</summary>
    internal byte[] Guarded(byte[] body)
    {
        Secrets secrets;
        lock (_gate) secrets = _secrets;
        var status = JsonRedaction.SanitizeJson(body, JsonRedaction.Cleaner(secrets), MaxValues, out var cleaned, out _, out var changed);
        if (status != JsonWalkStatus.Ok) throw OutboundRejectedException.For(status);
        return changed ? cleaned : body;
    }
}

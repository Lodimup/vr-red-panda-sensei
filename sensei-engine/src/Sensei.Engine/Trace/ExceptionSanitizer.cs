using System;
using System.Collections.Generic;
using System.IO;
using Sensei.Engine.Storage;

namespace Sensei.Engine.Trace;

/// <summary>
/// An exception whose message carried a known secret or a credential-pattern match (S77), rebuilt with those redacted.
/// <see cref="OriginalType"/> names the type that was thrown, so the failure stays classifiable.
/// </summary>
public sealed class SanitizedException : Exception
{
    public SanitizedException(string originalType, string message, Exception? innerException)
        : base(message, innerException)
    {
        OriginalType = originalType;
    }

    /// <summary>Full name of the exception type that was replaced.</summary>
    public string OriginalType { get; }
}

/// <summary>
/// S77 at the engine boundary: an exception that leaves the engine (a store, a tool body, a speech port or a provider failed)
/// carries no known secret or credential-pattern match in its message or in any inner message. An exception with nothing to
/// redact is returned as is, so its type, identity and stack stay intact; otherwise a copy of the same type (or a
/// <see cref="SanitizedException"/> naming it) is built with the redacted messages.
/// </summary>
internal static class ExceptionSanitizer
{
    private const int MaxDepth = 16;
    private const string StackDataKey = "sensei.original_stack_trace";

    /// <summary><paramref name="ex"/> itself when it holds nothing to redact, else a redacted copy.</summary>
    public static Exception Sanitize(Exception ex, Secrets secrets) => Rebuild(ex, secrets, 0) ?? ex;

    /// <summary>For an exception filter: true, with the redacted copy, when <paramref name="ex"/> held something to redact.</summary>
    public static bool TryRewrite(Exception ex, Secrets secrets, out Exception safe)
    {
        var rebuilt = Rebuild(ex, secrets, 0);
        safe = rebuilt ?? ex;
        return rebuilt is not null;
    }

    /// <summary>The redacted copy, or null when neither the message nor any inner exception changes.</summary>
    private static Exception? Rebuild(Exception ex, Secrets secrets, int depth)
    {
        var message = secrets.Sanitize(ex.Message) ?? "";
        var changed = !string.Equals(message, ex.Message, StringComparison.Ordinal);

        Exception? inner = ex.InnerException;
        if (inner is not null && depth < MaxDepth)
        {
            var innerCopy = Rebuild(inner, secrets, depth + 1);
            if (innerCopy is not null)
            {
                inner = innerCopy;
                changed = true;
            }
        }

        List<Exception>? parts = null;
        if (ex is AggregateException aggregate && depth < MaxDepth)
        {
            parts = new List<Exception>(aggregate.InnerExceptions.Count);
            foreach (var part in aggregate.InnerExceptions)
            {
                var partCopy = Rebuild(part, secrets, depth + 1);
                if (partCopy is not null) changed = true;
                parts.Add(partCopy ?? part);
            }
        }

        if (!changed) return null;

        var copy = Create(ex, message, inner, parts, secrets);
        if (ex.StackTrace is { } stack) copy.Data[StackDataKey] = secrets.Sanitize(stack);
        return copy;
    }

    private static Exception Create(Exception original, string message, Exception? inner, List<Exception>? parts, Secrets secrets)
    {
        if (original is AggregateException && original.GetType() == typeof(AggregateException))
            return new AggregateException(message, parts ?? new List<Exception>());

        var type = original.GetType();
        if (original is ProfileCorruptException corrupt)
            return new ProfileCorruptException(secrets.Sanitize(corrupt.ProfileId) ?? "", secrets.Sanitize(corrupt.FilePath) ?? "", inner);
        if (type == typeof(IOException)) return new IOException(message, inner);
        if (type == typeof(InvalidOperationException)) return new InvalidOperationException(message, inner);
        if (type == typeof(OperationCanceledException)) return new OperationCanceledException(message, inner);
        if (type == typeof(TimeoutException)) return new TimeoutException(message, inner);
        if (type == typeof(UnauthorizedAccessException)) return new UnauthorizedAccessException(message, inner);
        if (type == typeof(FormatException)) return new FormatException(message, inner);
        if (type == typeof(NotSupportedException)) return new NotSupportedException(message, inner);
        return new SanitizedException(type.FullName ?? type.Name, message, inner);
    }
}

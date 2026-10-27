using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Storage;

namespace Sensei.Engine.Trace;

/// <summary>
/// S77 at the engine boundary. Every public <see cref="SenseiEngine"/> member passes what it catches through
/// <see cref="ForHost"/>, so the host never receives a dependency's exception object: not its message, its inner exceptions,
/// its data or its subclass members. Nothing is inspected below the root to decide that it is safe, because nothing below
/// the root is kept.
/// </summary>
internal static class ExceptionSanitizer
{
    private const string Unavailable = "(unavailable)";

    /// <summary>
    /// The exception the host sees for <paramref name="ex"/>. An intended public exception (<see cref="InvalidOperationException"/>,
    /// <see cref="ArgumentException"/>, <see cref="ArgumentNullException"/>, <see cref="ProfileCorruptException"/>,
    /// <see cref="OperationCanceledException"/>, <see cref="TaskCanceledException"/>, matched by exact type) is rebuilt with a sanitized
    /// message and no inner exception; every other exception becomes a <see cref="SenseiEngineException"/> with a sanitized message
    /// and no inner exception. <paramref name="detail"/> is the fully sanitized description of the original for the trace, or null
    /// when nothing worth tracing was dropped. Never throws.
    /// </summary>
    public static Exception ForHost(Exception ex, Secrets secrets, out string? detail)
    {
        detail = null;
        Exception safe;
        try
        {
            var intended = RebuildIntended(ex, secrets);
            safe = intended ?? new SenseiEngineException(Clean(ex.GetType().FullName ?? ex.GetType().Name, secrets), Clean(ex.Message, secrets));
            if (intended is null || ex.InnerException is not null) detail = TryDescribe(ex, secrets);
        }
        catch (Exception)
        {
            safe = new SenseiEngineException(Unavailable, "an engine operation failed");
            detail = null;
        }
        return safe;
    }

    /// <summary>
    /// Every string of <paramref name="root"/> and of everything reachable from it (inner exceptions, aggregate members) with known
    /// secrets and credential-pattern matches replaced. The graph is walked with an explicit stack, so no depth limit applies and a
    /// cycle is described once.
    /// </summary>
    public static string Describe(Exception root, Secrets secrets)
    {
        var text = new StringBuilder();
        var seen = new Dictionary<Exception, int>(ReferenceComparer.Instance);
        var pending = new Stack<(Exception Ex, int ParentIndex)>();
        pending.Push((root, -1));
        var children = new List<Exception>();
        while (pending.Count > 0)
        {
            var (current, parentIndex) = pending.Pop();
            if (seen.TryGetValue(current, out var earlier))
            {
                text.Append("(").Append(parentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" refers back to ")
                    .Append(earlier.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(")\n");
                continue;
            }
            var index = seen.Count;
            seen[current] = index;

            text.Append('[').Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(']');
            if (parentIndex >= 0) text.Append(" inside [").Append(parentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(']');
            text.Append(' ').Append(Clean(current.GetType().FullName ?? current.GetType().Name, secrets))
                .Append(": ").Append(Read(() => current.Message, secrets)).Append('\n');
            var stack = Read(() => current.StackTrace, secrets);
            if (stack.Length > 0) text.Append(stack).Append('\n');
            var source = Read(() => current.Source, secrets);
            if (source.Length > 0) text.Append("source: ").Append(source).Append('\n');
            var help = Read(() => current.HelpLink, secrets);
            if (help.Length > 0) text.Append("help: ").Append(help).Append('\n');
            AppendData(text, current, secrets);

            children.Clear();
            if (current is AggregateException aggregate)
            {
                foreach (var part in aggregate.InnerExceptions) children.Add(part);
            }
            else if (current.InnerException is { } inner)
            {
                children.Add(inner);
            }
            for (var i = children.Count - 1; i >= 0; i--) pending.Push((children[i], index));
        }
        return text.ToString();
    }

    private static string? TryDescribe(Exception ex, Secrets secrets)
    {
        try
        {
            return Describe(ex, secrets);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void AppendData(StringBuilder text, Exception ex, Secrets secrets)
    {
        try
        {
            foreach (DictionaryEntry entry in ex.Data)
                text.Append("data ").Append(Read(() => entry.Key?.ToString(), secrets)).Append(" = ").Append(Read(() => entry.Value?.ToString(), secrets)).Append('\n');
        }
        catch (Exception)
        {
            text.Append("data: ").Append(Unavailable).Append('\n');
        }
    }

    private static Exception? RebuildIntended(Exception ex, Secrets secrets)
    {
        var type = ex.GetType();
        if (type == typeof(InvalidOperationException)) return new InvalidOperationException(Clean(ex.Message, secrets));
        if (type == typeof(ArgumentNullException))
        {
            var param = ((ArgumentException)ex).ParamName;
            return new ArgumentNullException(CleanOrNull(param, secrets), Clean(WithoutParameter(ex.Message, param), secrets));
        }
        if (type == typeof(ArgumentException))
        {
            var param = ((ArgumentException)ex).ParamName;
            return new ArgumentException(Clean(WithoutParameter(ex.Message, param), secrets), CleanOrNull(param, secrets));
        }
        if (ex is ProfileCorruptException corrupt)
            return new ProfileCorruptException(Clean(corrupt.ProfileId, secrets), Clean(corrupt.FilePath, secrets), null);
        if (type == typeof(OperationCanceledException))
            return new OperationCanceledException(Clean(ex.Message, secrets), ((OperationCanceledException)ex).CancellationToken);
        if (type == typeof(TaskCanceledException))
            return new TaskCanceledException(Clean(ex.Message, secrets), null, ((TaskCanceledException)ex).CancellationToken);
        return null;
    }

    /// <summary><see cref="ArgumentException.Message"/> appends <c> (Parameter 'x')</c>; the rebuilt exception adds it again from its parameter name.</summary>
    private static string WithoutParameter(string message, string? paramName)
    {
        if (string.IsNullOrEmpty(paramName)) return message;
        var suffix = " (Parameter '" + paramName + "')";
        return message.EndsWith(suffix, StringComparison.Ordinal) ? message.Substring(0, message.Length - suffix.Length) : message;
    }

    private static string Clean(string? text, Secrets secrets) => secrets.Sanitize(text) ?? "";

    private static string? CleanOrNull(string? text, Secrets secrets) => text is null ? null : secrets.Sanitize(text);

    /// <summary>A sanitized string read from a member that may itself throw.</summary>
    private static string Read(Func<string?> get, Secrets secrets)
    {
        try
        {
            return Clean(get(), secrets);
        }
        catch (Exception)
        {
            return Unavailable;
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<Exception>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(Exception? x, Exception? y) => ReferenceEquals(x, y);

        public int GetHashCode(Exception obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

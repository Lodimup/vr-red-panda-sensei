using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Storage;

namespace Sensei.Engine.Trace;

/// <summary>
/// S77 at the engine boundary. Every public <see cref="SenseiEngine"/> member passes what it catches through
/// <see cref="ForHost"/>. What the host receives is written by the engine: a failure of a dependency is reported by operation
/// and category only, and nothing of the dependency's exception (its message, inner exceptions, data, subclass members) is read to
/// build it. The trace gets a bounded description that reads no more than the type name and, for a closed list of framework types
/// whose message is a stored string, that message.
/// </summary>
internal static class ExceptionSanitizer
{
    /// <summary>What a trace line says for a property that could not be read.</summary>
    internal const string Unavailable = "[unavailable]";

    /// <summary>S77: the most exceptions one trace description visits.</summary>
    internal const int MaxNodes = 32;

    /// <summary>The exact types whose <see cref="Exception.Message"/> is a stored string and may be recorded. No other type's message is ever read.</summary>
    private static readonly HashSet<Type> MessageTypes = new()
    {
        typeof(IOException),
        typeof(FileNotFoundException),
        typeof(DirectoryNotFoundException),
        typeof(UnauthorizedAccessException),
        typeof(TimeoutException),
        typeof(HttpRequestException),
        typeof(JsonException),
        typeof(ArgumentException),
        typeof(InvalidOperationException),
        typeof(NotSupportedException),
        typeof(FormatException),
    };

    /// <summary>
    /// The exception the host sees for <paramref name="ex"/>, written by the engine from <paramref name="operation"/> (a public member
    /// name) and a fixed category. An exception the engine threw on purpose (<see cref="InvalidOperationException"/> for
    /// <c>turn_in_progress</c>, <see cref="ArgumentException"/>, <see cref="ArgumentNullException"/>) and the documented
    /// <see cref="ProfileCorruptException"/> and <see cref="OperationCanceledException"/> arrive as their own type with an
    /// engine-authored message; so does a dependency's exception of exactly one of those types. Everything else becomes a
    /// <see cref="SenseiEngineException"/> whose message is <c>&lt;operation&gt; failed: &lt;category&gt;</c>. Neither has an inner
    /// exception. <paramref name="detail"/> is the bounded, sanitized description of the original for the trace, or null when nothing
    /// worth tracing was dropped. Never throws.
    /// </summary>
    public static Exception ForHost(Exception ex, Secrets secrets, out string? detail, string operation = "operation", string? profileId = null)
    {
        detail = null;
        var name = OperationName(operation);
        try
        {
            if (ex is SenseiEngineException already) return already;
            var category = FaultCategories.Of(ex);
            var message = name + " failed: " + category;
            var type = ex.GetType();
            var authored = Authored.TryGet(ex, out var authoredMessage, out var paramName);
            Exception? intended = null;
            if (type == typeof(InvalidOperationException)) intended = new InvalidOperationException(authored ? authoredMessage : message);
            else if (type == typeof(ArgumentNullException)) intended = authored ? new ArgumentNullException(paramName) : new ArgumentNullException(null, message);
            else if (type == typeof(ArgumentException)) intended = authored ? new ArgumentException(authoredMessage, paramName) : new ArgumentException(message);
            else if (ex is ProfileCorruptException)
            {
                var id = profileId ?? "profile";
                intended = new ProfileCorruptException(id, id + ".json", null);
            }
            else if (type == typeof(OperationCanceledException) || type == typeof(TaskCanceledException))
            {
                intended = new OperationCanceledException(name + " was canceled", ((OperationCanceledException)ex).CancellationToken);
            }

            var traced = intended is null || ex.InnerException is not null;
            if (traced) detail = TryDescribe(ex, secrets);
            return intended ?? new SenseiEngineException(Clean(type.FullName ?? type.Name, secrets), message);
        }
        catch (Exception)
        {
            detail = null;
            return new SenseiEngineException(Unavailable, name + " failed: " + FaultCategories.Internal);
        }
    }

    /// <summary>
    /// A bounded description of <paramref name="root"/> for the trace: at most <see cref="MaxNodes"/> exceptions, visited with an
    /// explicit stack and a visited set (a cycle is described once). Each node records its type full name and, only for the exact
    /// types in <see cref="MessageTypes"/>, its message. An aggregate is walked through its inner exceptions and its own message is
    /// never read, because it formats every child recursively. Every string is sanitized; a property that cannot be read records
    /// <see cref="Unavailable"/>.
    /// </summary>
    public static string Describe(Exception root, Secrets secrets)
    {
        var text = new StringBuilder();
        var seen = new Dictionary<Exception, int>(ReferenceComparer.Instance);
        var pending = new Stack<(Exception Ex, int ParentIndex)>();
        pending.Push((root, -1));
        var truncated = false;
        var children = new List<Exception>();
        while (pending.Count > 0)
        {
            var (current, parentIndex) = pending.Pop();
            if (seen.TryGetValue(current, out var earlier))
            {
                text.Append('(').Append(parentIndex.ToString(CultureInfo.InvariantCulture)).Append(" refers back to ")
                    .Append(earlier.ToString(CultureInfo.InvariantCulture)).Append(")\n");
                continue;
            }
            if (seen.Count >= MaxNodes)
            {
                truncated = true;
                continue;
            }
            var index = seen.Count;
            seen[current] = index;

            text.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
            if (parentIndex >= 0) text.Append(" inside [").Append(parentIndex.ToString(CultureInfo.InvariantCulture)).Append(']');
            text.Append(' ').Append(Summary(current, secrets)).Append('\n');

            children.Clear();
            var room = MaxNodes - seen.Count - pending.Count;
            if (!TryCollectChildren(current, children, room)) truncated = true;
            for (var i = children.Count - 1; i >= 0; i--) pending.Push((children[i], index));
        }
        if (truncated) text.Append("(more exceptions not shown)\n");
        return text.ToString();
    }

    /// <summary>
    /// One exception on one line: its sanitized type full name and, only for the exact types in <see cref="MessageTypes"/>,
    /// <c>: &lt;sanitized message&gt;</c>. Reads nothing else. Never throws.
    /// </summary>
    public static string Summary(Exception ex, Secrets secrets)
    {
        try
        {
            var type = Read(() => ex.GetType().FullName ?? ex.GetType().Name, secrets);
            return IsMessageType(ex) ? type + ": " + Read(() => ex.Message, secrets) : type;
        }
        catch (Exception)
        {
            return Unavailable;
        }
    }

    /// <summary>
    /// Why a dependency's exception was handled in place (the speech port): its sanitized message when its type is one of
    /// <see cref="MessageTypes"/>, else its sanitized type name. Never throws.
    /// </summary>
    public static string Reason(Exception ex, Secrets secrets)
    {
        try
        {
            var type = Read(() => ex.GetType().FullName ?? ex.GetType().Name, secrets);
            return IsMessageType(ex) ? Read(() => ex.Message, secrets) : type;
        }
        catch (Exception)
        {
            return Unavailable;
        }
    }

    /// <summary>The public member name as it reads in an error message: <c>SetStartingBandAsync</c> → <c>set_starting_band</c>.</summary>
    internal static string OperationName(string member)
    {
        if (string.IsNullOrEmpty(member)) return "operation";
        var name = member.EndsWith("Async", StringComparison.Ordinal) && member.Length > "Async".Length ? member.Substring(0, member.Length - "Async".Length) : member;
        var text = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) text.Append('_');
                text.Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }
        return text.ToString();
    }

    private static bool IsMessageType(Exception ex) => MessageTypes.Contains(ex.GetType());

    /// <summary>Collects at most <paramref name="room"/> children of <paramref name="ex"/>; false when some were left out or could not be read.</summary>
    private static bool TryCollectChildren(Exception ex, List<Exception> children, int room)
    {
        try
        {
            if (ex is AggregateException aggregate)
            {
                foreach (var part in aggregate.InnerExceptions)
                {
                    if (children.Count >= room) return false;
                    if (part is not null) children.Add(part);
                }
            }
            else if (ex.InnerException is { } inner)
            {
                if (room <= 0) return false;
                children.Add(inner);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
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

    private static string Clean(string? text, Secrets secrets) => secrets.Sanitize(text) ?? "";

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

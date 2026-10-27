using System;
using System.ClientModel;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Agent;

/// <summary>
/// S67: maps a provider failure to a turn error code by HTTP status, then by kind of failure.
/// <para>
/// The provider's exception is foreign input (S77, D2): the graph is walked iteratively with a visited set and at most
/// <see cref="ExceptionSanitizer.MaxNodes"/> nodes, every property is read inside a guard, the status comes from properties and
/// never from formatted text, and a <c>Message</c> is read only for the closed list of framework types whose message is a stored
/// string (<see cref="ExceptionSanitizer.Reason"/>). The detail is sanitized first and shortened after, so a cut can never leave the
/// prefix of a secret behind.
/// </para>
/// </summary>
internal static class ProviderErrors
{
    private const int MaxDetailLength = 300;

    /// <summary>401/403 → auth_failed, 402/429 → quota_exceeded, connection failures and timeouts → network_error, anything else → provider_error.</summary>
    public static (string Code, string Detail) Map(Exception error, Secrets secrets)
    {
        var graph = Inspect(error);
        var detail = Describe(graph, secrets);
        switch (graph.Status)
        {
            case 401:
            case 403:
                return (TurnErrorCodes.AuthFailed, detail);
            case 402:
            case 429:
                return (TurnErrorCodes.QuotaExceeded, detail);
        }
        if (graph.Status is null && graph.NetworkFailure) return (TurnErrorCodes.NetworkError, detail);
        return (TurnErrorCodes.ProviderError, detail);
    }

    /// <summary>The HTTP status carried by the exception or by any exception in its (bounded) graph, if any.</summary>
    internal static int? FindStatus(Exception error) => Inspect(error).Status;

    /// <summary>What the bounded walk over an exception graph found.</summary>
    private sealed class Graph
    {
        public Exception Primary = null!;
        public int? Status;
        public bool NetworkFailure;
    }

    private static Graph Inspect(Exception root)
    {
        var graph = new Graph { Primary = Primary(root) };
        // Pre-order, first member first, like ExceptionSanitizer.Describe (D2): the node budget is spent along the first branch.
        var seen = new HashSet<Exception>(ReferenceComparer.Instance);
        var pending = new Stack<Exception>();
        pending.Push(root);
        var children = new List<Exception>();
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (seen.Contains(current)) continue;
            if (seen.Count >= ExceptionSanitizer.MaxNodes) break;
            seen.Add(current);
            Observe(current, graph);
            children.Clear();
            Children(current, children);
            for (var i = children.Count - 1; i >= 0; i--) pending.Push(children[i]);
        }
        return graph;
    }

    /// <summary>Reads the status and the kind of failure one node stands for. Nothing it reads can throw out of here.</summary>
    private static void Observe(Exception node, Graph graph)
    {
        try
        {
            if (node is ClientResultException client)
            {
                if (client.Status > 0) graph.Status ??= client.Status;
                else graph.NetworkFailure = true;
                return;
            }
            if (node is HttpRequestException http)
            {
                graph.NetworkFailure = true;
                if (ReadHttpRequestStatus(http) is { } code) graph.Status ??= code;
                return;
            }
            if (node is SocketException || node is IOException || node is WebException || node is TimeoutException || node is OperationCanceledException)
                graph.NetworkFailure = true;
        }
        catch (Exception)
        {
            // A node that cannot be read says nothing.
        }
    }

    private static void Children(Exception node, List<Exception> into)
    {
        try
        {
            if (node is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    if (inner is not null) into.Add(inner);
            }
            else if (node.InnerException is { } single)
            {
                into.Add(single);
            }
        }
        catch (Exception)
        {
            // An exception whose members throw contributes no children.
        }
    }

    // HttpRequestException.StatusCode is not in netstandard2.1; the host runtime may still provide it.
    private static int? ReadHttpRequestStatus(HttpRequestException error)
    {
        try
        {
            var property = error.GetType().GetProperty("StatusCode");
            if (property?.GetValue(error) is HttpStatusCode code && (int)code > 0) return (int)code;
        }
        catch (Exception)
        {
            // Not readable on this runtime: fall through to "no status".
        }
        return null;
    }

    /// <summary>The exception the detail is about: the root with single-member aggregates peeled off (bounded, cycle-safe).</summary>
    private static Exception Primary(Exception root)
    {
        var seen = new HashSet<Exception>(ReferenceComparer.Instance);
        var current = root;
        try
        {
            while (current is AggregateException { InnerExceptions.Count: 1 } aggregate && seen.Count < ExceptionSanitizer.MaxNodes && seen.Add(current))
            {
                var inner = aggregate.InnerExceptions[0];
                if (inner is null) break;
                current = inner;
            }
        }
        catch (Exception)
        {
            // Keep what was reached.
        }
        return current;
    }

    private static string Describe(Graph graph, Secrets secrets)
    {
        var reason = ExceptionSanitizer.Reason(graph.Primary, secrets);
        if (string.IsNullOrWhiteSpace(reason)) reason = graph.Primary.GetType().Name;
        var text = graph.Status is > 0 ? $"HTTP {graph.Status}: {reason}" : reason;
        // Sanitize the whole diagnostic, then shorten it (S77): a cut made first could leave a key prefix that no longer matches.
        text = (secrets.Sanitize(text) ?? "").Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= MaxDetailLength ? text : text.Substring(0, MaxDetailLength);
    }

    private sealed class ReferenceComparer : IEqualityComparer<Exception>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(Exception? x, Exception? y) => ReferenceEquals(x, y);

        public int GetHashCode(Exception obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

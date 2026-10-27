using System;
using System.ClientModel;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sensei.Engine.Llm;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Agent;

/// <summary>
/// S67: maps a provider failure to a turn error code by HTTP status, then by kind of failure.
/// <para>
/// The provider's exception is foreign input (S77, D2): the graph is walked iteratively with a visited set and at most
/// <see cref="ExceptionSanitizer.MaxNodes"/> nodes, every read is inside a guard, the status comes only from the public property of an exact
/// <see cref="ClientResultException"/> or <see cref="HttpRequestException"/> (never from formatted text or a subclass), and a <c>Message</c> is read only for the closed list of framework types whose message is a stored
/// string (<see cref="ExceptionSanitizer.Reason"/>). The detail is sanitized first and shortened after, so a cut can never leave the
/// prefix of a secret behind.
/// </para>
/// </summary>
internal static class ProviderErrors
{
    private const int MaxDetailLength = 300;

    /// <summary>
    /// <c>HttpRequestException.StatusCode</c> of the framework type itself, looked up once; null on a runtime that has none. Read only for
    /// a node of exactly that type, so the getter that runs is the framework's, never a subclass's.
    /// </summary>
    private static readonly PropertyInfo? HttpRequestStatus = FindHttpRequestStatus();

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

    private static PropertyInfo? FindHttpRequestStatus()
    {
        try
        {
            return typeof(HttpRequestException).GetProperty("StatusCode");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The HTTP status carried by the exception or by any exception in its (bounded) graph, if any.</summary>
    internal static int? FindStatus(Exception error) => Inspect(error).Status;

    /// <summary>
    /// D6: the reason, when the outbound guard refused to send the request and its exception (an exact <see cref="OutboundRejectedException"/>,
    /// which is the engine's own) is anywhere in the bounded graph; otherwise null.
    /// </summary>
    internal static string? OutboundRejection(Exception error) => Inspect(error).Rejection;

    /// <summary>What the bounded walk over an exception graph found.</summary>
    private sealed class Graph
    {
        public Exception Primary = null!;
        public int? Status;
        public bool NetworkFailure;
        public string? Rejection;
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

    /// <summary>
    /// Reads the status and the kind of failure one node stands for. An HTTP status is read from exactly two kinds of node, both by
    /// exact type (S132): the public <c>Status</c> of a <see cref="ClientResultException"/> (what the OpenAI client raises for every HTTP
    /// error response; it exists on every target), and the <c>StatusCode</c> of an <see cref="HttpRequestException"/> through the one
    /// cached <see cref="HttpRequestStatus"/> property of that framework type, which is absent on runtimes without it (netstandard2.1
    /// has none) and then says nothing. The getter that runs is the framework's own. A subclass of either type, and every other
    /// exception, is classified by its type alone, which runs no foreign code: no member of it is read and no reflection touches it.
    /// </summary>
    private static void Observe(Exception node, Graph graph)
    {
        try
        {
            if (node.GetType() == typeof(OutboundRejectedException))
            {
                graph.Rejection ??= ((OutboundRejectedException)node).Reason;
                return;
            }
            if (node.GetType() == typeof(ClientResultException))
            {
                var status = ((ClientResultException)node).Status;
                if (status > 0) graph.Status ??= status;
                else graph.NetworkFailure = true;
                return;
            }
            if (node.GetType() == typeof(HttpRequestException))
            {
                graph.NetworkFailure = true;
                if (HttpRequestStatus?.GetValue(node) is HttpStatusCode code && (int)code > 0) graph.Status ??= (int)code;
                return;
            }
            if (node is HttpRequestException || node is SocketException || node is IOException || node is WebException
                || node is TimeoutException || node is OperationCanceledException)
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
        if (graph.Rejection is { } rejection) return "outbound request rejected: " + rejection;
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

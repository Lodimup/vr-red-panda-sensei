using System;
using System.ClientModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Sensei.Engine.Agent;

/// <summary>S67: maps a provider failure to a turn error code by HTTP status, then by kind of failure.</summary>
internal static class ProviderErrors
{
    private const int MaxDetailLength = 300;

    /// <summary>401/403 → auth_failed, 402/429 → quota_exceeded, connection failures and timeouts → network_error, anything else → provider_error.</summary>
    public static (string Code, string Detail) Map(Exception error)
    {
        var status = FindStatus(error);
        var detail = Describe(error, status);
        switch (status)
        {
            case 401:
            case 403:
                return (TurnErrorCodes.AuthFailed, detail);
            case 402:
            case 429:
                return (TurnErrorCodes.QuotaExceeded, detail);
        }
        if (status is null or 0 && IsNetworkFailure(error)) return (TurnErrorCodes.NetworkError, detail);
        return (TurnErrorCodes.ProviderError, detail);
    }

    /// <summary>The HTTP status carried by the exception or by one of its inner exceptions, if any.</summary>
    internal static int? FindStatus(Exception error)
    {
        for (var e = Unwrap(error); e is not null; e = e.InnerException)
        {
            if (e is ClientResultException client && client.Status > 0) return client.Status;
            if (e is HttpRequestException http && ReadHttpRequestStatus(http) is { } code) return code;
        }
        return null;
    }

    // HttpRequestException.StatusCode is not in netstandard2.1; the host runtime may still provide it.
    private static int? ReadHttpRequestStatus(HttpRequestException error)
    {
        try
        {
            var property = error.GetType().GetProperty("StatusCode");
            if (property?.GetValue(error) is HttpStatusCode code) return (int)code;
        }
        catch (Exception)
        {
            // Not readable on this runtime: fall through to "no status".
        }
        return null;
    }

    private static bool IsNetworkFailure(Exception error)
    {
        for (var e = Unwrap(error); e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException || e is SocketException || e is IOException || e is WebException
                || e is TimeoutException || e is OperationCanceledException)
                return true;
            if (e is ClientResultException client && client.Status == 0) return true;
        }
        return false;
    }

    private static Exception Unwrap(Exception error)
    {
        var e = error;
        while (e is AggregateException { InnerExceptions.Count: 1 } aggregate) e = aggregate.InnerExceptions[0];
        return e;
    }

    private static string Describe(Exception error, int? status)
    {
        var message = Unwrap(error).Message;
        if (string.IsNullOrWhiteSpace(message)) message = Unwrap(error).GetType().Name;
        var text = status is > 0 ? $"HTTP {status}: {message}" : message;
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= MaxDetailLength ? text : text.Substring(0, MaxDetailLength);
    }
}

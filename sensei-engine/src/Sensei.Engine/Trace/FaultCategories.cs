using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sensei.Engine.Trace;

/// <summary>
/// S77: the fixed set of failure categories a <see cref="SenseiEngineException"/> can name. The host only ever sees one of
/// these words, never any text a failing dependency produced.
/// </summary>
internal static class FaultCategories
{
    public const string ProfileStore = "profile_store_error";
    public const string SpeechOutput = "speech_output_error";
    public const string EventHandler = "event_handler_error";
    public const string Llm = "llm_error";
    public const string Internal = "internal_error";
    public const string InvalidConfiguration = "invalid_configuration";

    /// <summary>Every category, for the tests that pin the set.</summary>
    public static IReadOnlyList<string> All { get; } = new[] { ProfileStore, SpeechOutput, EventHandler, Llm, Internal, InvalidConfiguration };

    private static readonly ConditionalWeakTable<Exception, string> Tags = new();

    /// <summary>
    /// Records which dependency <paramref name="ex"/> came from while it passes the engine's own call site. The exception object is
    /// not changed and nothing is read from it. The first tag wins, so the innermost call site names the cause.
    /// </summary>
    public static void Tag(Exception ex, string category) => Tags.GetValue(ex, _ => category);

    /// <summary>The category the engine tagged <paramref name="ex"/> with, or <see cref="Internal"/>.</summary>
    public static string Of(Exception ex) => Tags.TryGetValue(ex, out var category) ? category : Internal;
}

/// <summary>
/// The exceptions the engine itself throws on purpose (a misused argument, <c>turn_in_progress</c>, a corrupt stored profile).
/// Each is created here with its message, and the message is remembered by identity, so the boundary can tell an exception the
/// engine authored from a dependency's exception of the same type without reading either one's text.
/// </summary>
internal static class Authored
{
    private static readonly ConditionalWeakTable<Exception, Record> Records = new();

    private sealed record Record(string Message, string? ParamName);

    public static InvalidOperationException InvalidOperation(string message) => Register(new InvalidOperationException(message), message, null);

    public static ArgumentException Argument(string message, string paramName) => Register(new ArgumentException(message, paramName), message, paramName);

    public static ArgumentNullException ArgumentNull(string paramName) => Register(new ArgumentNullException(paramName), "", paramName);

    /// <summary>The message and parameter name the engine gave <paramref name="ex"/>, or false when the engine did not author it.</summary>
    public static bool TryGet(Exception ex, out string message, out string? paramName)
    {
        if (Records.TryGetValue(ex, out var record))
        {
            message = record.Message;
            paramName = record.ParamName;
            return true;
        }
        message = "";
        paramName = null;
        return false;
    }

    private static T Register<T>(T ex, string message, string? paramName) where T : Exception
    {
        Records.GetValue(ex, _ => new Record(message, paramName));
        return ex;
    }
}

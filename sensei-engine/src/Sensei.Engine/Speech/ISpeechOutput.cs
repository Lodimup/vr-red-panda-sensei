using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sensei.Engine.Speech;

/// <summary>The speech port Unity's local TTS implements (SPEC §8).</summary>
public interface ISpeechOutput
{
    Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct);
}

/// <summary>One spoken segment. <c>Lang</c> is <c>ja</c> or <c>en</c>.</summary>
public sealed record SpeechSegment(string Lang, string Text, string? Reading, string? Romaji, string? Translation);

/// <summary>Effective caption visibility for a speech request (S91).</summary>
public sealed record SpeechCaptions(bool ShowJapanese, bool ShowReading, bool ShowRomaji, bool ShowTranslation)
{
    /// <summary>Everything visible.</summary>
    public static SpeechCaptions All { get; } = new(true, true, true, true);
}

/// <summary>A request to speak (S37d, S91).</summary>
public sealed record SpeechRequest(string UtteranceId, long GenerationId, IReadOnlyList<SpeechSegment> Segments, string Pace, SpeechCaptions? Captions = null)
{
    /// <summary>Caption visibility; all visible when the caller did not set it.</summary>
    public SpeechCaptions EffectiveCaptions => Captions ?? SpeechCaptions.All;
}

/// <summary>Outcome of a speech request. <c>Status</c>: <c>completed</c> | <c>cancelled</c> | <c>failed</c> (S37d, S89).</summary>
public sealed record SpeechOutcome(string Status, string? Error, int? SegmentsPresented = null)
{
    /// <summary>
    /// Segments that reached the learner, clamped to 0..<paramref name="segmentCount"/>.
    /// An explicit <c>SegmentsPresented</c> always wins; a <c>completed</c> outcome without one counts as every segment presented.
    /// </summary>
    public int PresentedOf(int segmentCount)
    {
        if (SegmentsPresented is { } explicitCount) return Math.Max(0, Math.Min(segmentCount, explicitCount));
        return Status == SpeechStatuses.Completed ? segmentCount : 0;
    }
}

/// <summary>Speech outcome statuses.</summary>
public static class SpeechStatuses
{
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

/// <summary>Speech languages and paces.</summary>
public static class SpeechLanguages
{
    public const string Japanese = "ja";
    public const string English = "en";
}

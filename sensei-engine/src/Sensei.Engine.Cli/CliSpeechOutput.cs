using Sensei.Engine.Speech;

namespace Sensei.Engine.Cli;

/// <summary>
/// The speech port of the CLI (S70, S91): prints one line per segment — <c>Sensei [ja]: &lt;text&gt;</c> or
/// <c>Sensei [en]: &lt;text&gt;</c> — honouring the caption settings (a hidden Japanese segment prints as
/// <c>Sensei [ja]: (audio)</c>), and with <c>--voice say</c> also speaks each segment aloud.
/// </summary>
internal sealed class CliSpeechOutput : ISpeechOutput
{
    private readonly CliOutput _output;
    private readonly SayVoice? _voice;

    public CliSpeechOutput(CliOutput output, SayVoice? voice)
    {
        _output = output;
        _voice = voice;
    }

    public async Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct)
    {
        var captions = request.EffectiveCaptions;
        var presented = 0;
        foreach (var segment in request.Segments)
        {
            if (ct.IsCancellationRequested) return new SpeechOutcome(SpeechStatuses.Cancelled, null, presented);
            _output.Line(FormatSegment(segment, captions));
            presented++;
            if (_voice is null) continue;

            var problem = await _voice.SpeakAsync(segment.Lang, segment.Text, request.Pace, ct).ConfigureAwait(false);
            if (problem == "cancelled") return new SpeechOutcome(SpeechStatuses.Cancelled, null, presented);
            if (problem is not null) return new SpeechOutcome(SpeechStatuses.Failed, problem, presented);
        }
        return new SpeechOutcome(SpeechStatuses.Completed, null, presented);
    }

    /// <summary>One printed line for one segment.</summary>
    internal static string FormatSegment(SpeechSegment segment, SpeechCaptions captions)
    {
        if (segment.Lang != SpeechLanguages.Japanese) return $"Sensei [{segment.Lang}]: {segment.Text}";

        var line = $"Sensei [ja]: {(captions.ShowJapanese ? segment.Text : "(audio)")}";
        if (captions.ShowReading && !string.IsNullOrEmpty(segment.Reading)) line += $" | reading: {segment.Reading}";
        if (captions.ShowRomaji && !string.IsNullOrEmpty(segment.Romaji)) line += $" | romaji: {segment.Romaji}";
        if (captions.ShowTranslation && !string.IsNullOrEmpty(segment.Translation)) line += $" | translation: {segment.Translation}";
        return line;
    }
}

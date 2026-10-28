using System.Text.Json;
using Sensei.Engine.Cli;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Cli;

public sealed class CliOutputTests
{
    private static readonly SpeechSegment Japanese = new("ja", "水をください", "みずをください", "mizu o kudasai", "Water, please");
    private static readonly SpeechSegment English = new("en", "Try saying it.", null, null, null);

    // ---------------------------------------------------------------- S70 / S91 speech lines

    [Fact]
    public void S70_a_japanese_segment_prints_as_sensei_ja_with_all_captions_on()
    {
        var line = CliSpeechOutput.FormatSegment(Japanese, SpeechCaptions.All);
        Assert.StartsWith("Sensei [ja]: 水をください", line);
        Assert.Contains("reading: みずをください", line);
        Assert.Contains("romaji: mizu o kudasai", line);
        Assert.Contains("translation: Water, please", line);
    }

    [Fact]
    public void S70_an_english_segment_prints_as_sensei_en()
    {
        Assert.Equal("Sensei [en]: Try saying it.", CliSpeechOutput.FormatSegment(English, SpeechCaptions.All));
    }

    [Fact]
    public void S91_hidden_japanese_prints_as_audio()
    {
        var line = CliSpeechOutput.FormatSegment(Japanese, new SpeechCaptions(false, true, true, true));
        Assert.StartsWith("Sensei [ja]: (audio)", line);
        Assert.DoesNotContain("水をください", line);
        Assert.Contains("reading: みずをください", line);
    }

    [Fact]
    public void S91_each_caption_shows_only_when_its_setting_is_on()
    {
        var onlyJapanese = CliSpeechOutput.FormatSegment(Japanese, new SpeechCaptions(true, false, false, false));
        Assert.Equal("Sensei [ja]: 水をください", onlyJapanese);

        var onlyReading = CliSpeechOutput.FormatSegment(Japanese, new SpeechCaptions(false, true, false, false));
        Assert.Equal("Sensei [ja]: (audio) | reading: みずをください", onlyReading);

        var onlyRomaji = CliSpeechOutput.FormatSegment(Japanese, new SpeechCaptions(false, false, true, false));
        Assert.Equal("Sensei [ja]: (audio) | romaji: mizu o kudasai", onlyRomaji);

        var onlyTranslation = CliSpeechOutput.FormatSegment(Japanese, new SpeechCaptions(false, false, false, true));
        Assert.Equal("Sensei [ja]: (audio) | translation: Water, please", onlyTranslation);

        Assert.Equal("Sensei [ja]: (audio)", CliSpeechOutput.FormatSegment(Japanese, new SpeechCaptions(false, false, false, false)));
    }

    [Fact]
    public void S91_a_segment_without_optional_fields_has_no_empty_captions()
    {
        var line = CliSpeechOutput.FormatSegment(new SpeechSegment("ja", "はい", null, null, null), SpeechCaptions.All);
        Assert.Equal("Sensei [ja]: はい", line);
    }

    [Fact]
    public void S91_english_text_is_never_hidden_by_japanese_captions()
    {
        Assert.Equal("Sensei [en]: Try saying it.", CliSpeechOutput.FormatSegment(English, new SpeechCaptions(false, false, false, false)));
    }

    [Fact]
    public async Task S70_the_speech_port_prints_one_line_per_segment_and_reports_all_presented()
    {
        var writer = new StringWriter();
        var port = new CliSpeechOutput(new CliOutput(writer), null);

        var outcome = await port.SpeakAsync(new SpeechRequest("u1", 1, new[] { Japanese, English }, "normal"), CancellationToken.None);

        Assert.Equal("completed", outcome.Status);
        Assert.Equal(2, outcome.SegmentsPresented);
        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Sensei [ja]:", lines[0]);
        Assert.StartsWith("Sensei [en]:", lines[1]);
    }

    [Fact]
    public async Task S91_the_speech_port_honours_the_request_captions()
    {
        var writer = new StringWriter();
        var port = new CliSpeechOutput(new CliOutput(writer), null);
        await port.SpeakAsync(new SpeechRequest("u1", 1, new[] { Japanese }, "normal", new SpeechCaptions(false, false, false, false)), CancellationToken.None);
        Assert.Equal("Sensei [ja]: (audio)", writer.ToString().Trim());
    }

    [Fact]
    public async Task S66_a_cancelled_speech_request_reports_what_was_presented()
    {
        var writer = new StringWriter();
        var port = new CliSpeechOutput(new CliOutput(writer), null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await port.SpeakAsync(new SpeechRequest("u1", 1, new[] { Japanese, English }, "normal"), cts.Token);

        Assert.Equal("cancelled", outcome.Status);
        Assert.Equal(0, outcome.SegmentsPresented);
        Assert.Equal("", writer.ToString());
    }

    // ---------------------------------------------------------------- S70 hint cards and summary

    [Fact]
    public void S70_a_hint_card_prints_its_kind_and_the_fields_sensei_chose()
    {
        var card = new HintCard { Kind = "vocabulary_card", Japanese = "水", Reading = "みず", English = "water" };
        Assert.Equal("Card [vocabulary_card]: 水 / みず / water", CliEvents.FormatCard(card));
    }

    [Fact]
    public void S70_a_hint_card_with_one_field_prints_just_that_field()
    {
        Assert.Equal("Card [explanation]: Use を after the thing you want.", CliEvents.FormatCard(new HintCard { Kind = "explanation", English = "Use を after the thing you want." }));
    }

    [Fact]
    public void S70_the_events_print_hint_cards_and_the_summary()
    {
        var writer = new StringWriter();
        var events = new CliEvents(new CliOutput(writer));
        events.OnHint(new HintCard { Kind = "translation", English = "water" });
        events.OnSummary(new SessionSummary
        {
            SessionId = "s-1",
            Practiced = { new PracticedTarget { Key = "mizu", Dimension = "vocabulary", Label = "used_with_help" } },
            UnresolvedKeys = { "ocha" },
            NextSuggestion = "try asking for tea",
        });
        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Card [translation]: water", lines[0]);
        Assert.Equal("Summary: practiced mizu (used_with_help); still unclear: ocha; next: try asking for tea", lines[1]);
    }

    [Fact]
    public void S70_the_events_that_only_the_renderer_needs_print_nothing()
    {
        var writer = new StringWriter();
        var events = new CliEvents(new CliOutput(writer));
        events.OnSenseiState("thinking");
        events.OnHighlight(new[] { "a" }, 100);
        events.OnAnimate("point", null);
        events.OnSupportChanged(new SupportSettings());
        Assert.Equal("", writer.ToString());
    }

    // ---------------------------------------------------------------- output never interleaves

    [Fact]
    public async Task S109_concurrent_writers_never_interleave_inside_a_line()
    {
        var writer = new StringWriter();
        var output = new CliOutput(writer);
        var tasks = Enumerable.Range(0, 8).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++) output.Line($"writer {w} line {i} " + new string((char)('a' + w), 40));
        }));
        await Task.WhenAll(tasks);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1600, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^writer (\d) line \d+ (\w)\2{39}$", l.TrimEnd('\r')));
    }

    [Fact]
    public void S109_a_multi_line_message_is_written_as_one_block()
    {
        var writer = new StringWriter();
        new CliOutput(writer).Line("a\nb\r\nc");
        Assert.Equal(new[] { "a", "b", "c" }, writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    // ---------------------------------------------------------------- S72 / S73 trace sink

    private static TraceEntry Entry(string session, string tool, string status, string? code, long revision) => new()
    {
        At = DateTimeOffset.Parse("2026-10-28T01:00:00Z"),
        SessionId = session,
        TurnId = 1,
        GenerationId = 1,
        Round = 1,
        CallId = "c1",
        Tool = tool,
        Arguments = "{}",
        Status = status,
        ErrorCode = code,
        Revision = revision,
    };

    [Theory]
    [InlineData("inspect_scene", "success", null, 3, "[tool] inspect_scene success rev=3")]
    [InlineData("move_object", "failure", "invalid_destination", 7, "[tool] move_object failure invalid_destination rev=7")]
    [InlineData("speak", "cancelled", "cancelled", 0, "[tool] speak cancelled cancelled rev=0")]
    public void S72_the_tool_line_has_name_status_optional_error_code_and_revision(string tool, string status, string? code, long revision, string expected)
    {
        Assert.Equal(expected, CliTraceSink.ToolLine(tool, status, code, revision));
    }

    [Fact]
    public void S73_entries_are_appended_as_json_lines_named_for_the_session()
    {
        var dir = CliRunner.NewTempDirectory();
        var sink = new CliTraceSink(dir, new CliOutput(new StringWriter()), echo: false);

        sink.Write(Entry("s-test-1", "speak", "success", null, 2));
        sink.Write(Entry("s-test-1", "move_object", "failure", "invalid_destination", 2));
        sink.Write(Entry("s-test-2", "inspect_scene", "success", null, 0));

        var first = File.ReadAllLines(Path.Combine(dir, "traces", "s-test-1.jsonl"));
        Assert.Equal(2, first.Length);
        using var doc = JsonDocument.Parse(first[1]);
        Assert.Equal("move_object", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal("invalid_destination", doc.RootElement.GetProperty("error_code").GetString());
        Assert.Equal("s-test-1", doc.RootElement.GetProperty("session_id").GetString());
        Assert.Single(File.ReadAllLines(Path.Combine(dir, "traces", "s-test-2.jsonl")));
    }

    [Fact]
    public void S72_echo_prints_one_line_per_entry_and_suppression_silences_it()
    {
        var dir = CliRunner.NewTempDirectory();
        var writer = new StringWriter();
        var sink = new CliTraceSink(dir, new CliOutput(writer), echo: true);

        sink.Write(Entry("s-x", "speak", "success", null, 1));
        sink.SuppressEcho = true;
        sink.Write(Entry("s-x", "speak", "success", null, 1));
        sink.SuppressEcho = false;
        sink.Write(Entry("s-x", "speak", "failure", "speech_failed", 1));

        Assert.Equal(new[] { "[tool] speak success rev=1", "[tool] speak failure speech_failed rev=1" },
            writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray());
        Assert.Equal(3, File.ReadAllLines(Path.Combine(dir, "traces", "s-x.jsonl")).Length);
    }

    [Fact]
    public void S72_without_echo_nothing_is_printed()
    {
        var writer = new StringWriter();
        var sink = new CliTraceSink(CliRunner.NewTempDirectory(), new CliOutput(writer), echo: false);
        sink.Write(Entry("s-x", "speak", "success", null, 1));
        Assert.Equal("", writer.ToString());
    }
}

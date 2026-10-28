using Sensei.Engine.Cli;
using Sensei.Engine.Speech;

namespace Sensei.Engine.Tests.Cli;

/// <summary>
/// S118: <c>--voice say</c> runs the program named <c>say</c> found through <c>PATH</c>. These tests put a fake <c>say</c> script
/// first on <c>PATH</c> (restored afterwards); they share one collection so they never overlap.
/// </summary>
[Collection("say-path")]
public sealed class CliSayTests
{
    private const string LogVariable = "SENSEI_FAKE_SAY_LOG";
    private const string ModeVariable = "SENSEI_FAKE_SAY_MODE";

    private sealed class FakeSay : IDisposable
    {
        private readonly string _oldPath;
        private readonly string _oldLog;
        private readonly string? _oldMode;

        public FakeSay(bool hang = false, int exitCode = 0)
        {
            Directory = CliRunner.NewTempDirectory();
            Log = Path.Combine(Directory, "calls.log");
            var script = Path.Combine(Directory, "say");
            File.WriteAllText(script,
                "#!/bin/sh\n" +
                "printf '%s\\n' \"$@\" >> \"$" + LogVariable + "\"\n" +
                "printf -- '--call--\\n' >> \"$" + LogVariable + "\"\n" +
                "if [ \"$" + ModeVariable + "\" = hang ]; then exec sleep 30; fi\n" +
                "if [ \"$" + ModeVariable + "\" = fail ]; then echo 'voice not installed' >&2; exit " + (exitCode == 0 ? 3 : exitCode) + "; fi\n" +
                "exit 0\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            _oldPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            _oldLog = Environment.GetEnvironmentVariable(LogVariable) ?? "";
            _oldMode = Environment.GetEnvironmentVariable(ModeVariable);
            Environment.SetEnvironmentVariable(LogVariable, Log);
            Environment.SetEnvironmentVariable(ModeVariable, hang ? "hang" : exitCode != 0 ? "fail" : null);
            Environment.SetEnvironmentVariable("PATH", Directory + Path.PathSeparator + _oldPath);
        }

        public string Directory { get; }
        public string Log { get; }

        /// <summary>The argument lists of the calls made, in order.</summary>
        public IReadOnlyList<string[]> Calls()
        {
            if (!File.Exists(Log)) return Array.Empty<string[]>();
            var calls = new List<string[]>();
            var current = new List<string>();
            foreach (var line in File.ReadAllLines(Log))
            {
                if (line == "--call--")
                {
                    calls.Add(current.ToArray());
                    current = new List<string>();
                }
                else current.Add(line);
            }
            return calls;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", _oldPath);
            Environment.SetEnvironmentVariable(LogVariable, _oldLog.Length == 0 ? null : _oldLog);
            Environment.SetEnvironmentVariable(ModeVariable, _oldMode);
        }
    }

    [Fact]
    public async Task S118_japanese_is_spoken_by_kyoko_and_english_by_samantha_through_path()
    {
        using var say = new FakeSay();
        var voice = new SayVoice();

        Assert.Null(await voice.SpeakAsync("ja", "こんにちは", "normal", CancellationToken.None));
        Assert.Null(await voice.SpeakAsync("en", "hello", "normal", CancellationToken.None));

        var calls = say.Calls();
        Assert.Equal(new[] { "-v", "Kyoko", "こんにちは" }, calls[0]);
        Assert.Equal(new[] { "-v", "Samantha", "hello" }, calls[1]);
    }

    [Fact]
    public async Task S118_slow_pace_uses_speech_rate_120()
    {
        using var say = new FakeSay();
        await new SayVoice().SpeakAsync("ja", "ゆっくり", "slow", CancellationToken.None);
        Assert.Equal(new[] { "-v", "Kyoko", "-r", "120", "ゆっくり" }, say.Calls().Single());
    }

    [Fact]
    public async Task S118_text_that_starts_with_a_dash_is_not_taken_for_an_option()
    {
        using var say = new FakeSay();
        await new SayVoice().SpeakAsync("en", "-v is a flag", "normal", CancellationToken.None);
        Assert.Equal(new[] { "-v", "Samantha", "--", "-v is a flag" }, say.Calls().Single());
    }

    [Fact]
    public void S118_arguments_are_built_without_a_program_path()
    {
        Assert.Equal(new[] { "-v", "Kyoko", "x" }, SayVoice.Arguments("ja", "x", "normal"));
        Assert.Equal(new[] { "-v", "Samantha", "-r", "120", "x" }, SayVoice.Arguments("en", "x", "slow"));
    }

    [Fact]
    public async Task S118_the_speech_port_prints_then_speaks_each_segment_in_order()
    {
        using var say = new FakeSay();
        var writer = new StringWriter();
        var port = new CliSpeechOutput(new CliOutput(writer), new SayVoice());
        var request = new SpeechRequest("u1", 1, new[]
        {
            new SpeechSegment("ja", "水をください", null, null, null),
            new SpeechSegment("en", "Water, please.", null, null, null),
        }, "slow");

        var outcome = await port.SpeakAsync(request, CancellationToken.None);

        Assert.Equal("completed", outcome.Status);
        Assert.Equal(2, outcome.SegmentsPresented);
        Assert.Contains("Sensei [ja]: 水をください", writer.ToString());
        var calls = say.Calls();
        Assert.Equal(new[] { "-v", "Kyoko", "-r", "120", "水をください" }, calls[0]);
        Assert.Equal(new[] { "-v", "Samantha", "-r", "120", "Water, please." }, calls[1]);
    }

    [Fact]
    public async Task S118_a_hidden_japanese_caption_is_still_spoken()
    {
        using var say = new FakeSay();
        var writer = new StringWriter();
        var port = new CliSpeechOutput(new CliOutput(writer), new SayVoice());
        await port.SpeakAsync(new SpeechRequest("u1", 1, new[] { new SpeechSegment("ja", "水", null, null, null) }, "normal", new SpeechCaptions(false, false, false, false)), CancellationToken.None);

        Assert.Equal("Sensei [ja]: (audio)", writer.ToString().Trim());
        Assert.Equal("水", say.Calls().Single().Last());
    }

    [Fact]
    public async Task S118_a_failing_say_makes_the_speech_fail_after_the_text_was_shown()
    {
        using var say = new FakeSay(exitCode: 3);
        var writer = new StringWriter();
        var port = new CliSpeechOutput(new CliOutput(writer), new SayVoice());

        var outcome = await port.SpeakAsync(new SpeechRequest("u1", 1, new[] { new SpeechSegment("en", "hi", null, null, null) }, "normal"), CancellationToken.None);

        Assert.Equal("failed", outcome.Status);
        Assert.Contains("voice not installed", outcome.Error);
        Assert.Equal(1, outcome.SegmentsPresented);
    }

    [Fact]
    public async Task S118_a_missing_say_program_is_a_speech_failure_not_a_crash()
    {
        var old = Environment.GetEnvironmentVariable("PATH");
        var empty = CliRunner.NewTempDirectory();
        try
        {
            Environment.SetEnvironmentVariable("PATH", empty);
            var problem = await new SayVoice().SpeakAsync("en", "hi", "normal", CancellationToken.None);
            Assert.NotNull(problem);
            Assert.Contains("say", problem);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", old);
        }
    }

    [Fact]
    public async Task S66_cancelling_stops_say_and_reports_cancelled()
    {
        using var say = new FakeSay(hang: true);
        var port = new CliSpeechOutput(new CliOutput(new StringWriter()), new SayVoice());
        using var cts = new CancellationTokenSource();
        var speaking = port.SpeakAsync(new SpeechRequest("u1", 1, new[] { new SpeechSegment("en", "a long sentence", null, null, null), new SpeechSegment("en", "never", null, null, null) }, "normal"), cts.Token);
        await AgentWait(() => say.Calls().Count == 1);

        cts.Cancel();
        var outcome = await speaking.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("cancelled", outcome.Status);
        Assert.Equal(1, outcome.SegmentsPresented);
        Assert.Single(say.Calls());
    }

    private static async Task AgentWait(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}

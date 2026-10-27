using System.Text.RegularExpressions;

namespace Sensei.Engine.Tests.Integration.ReviewFinal2;

/// <summary>
/// D6: the guarantee holds because there is one place a provider client is built and one place a trace line is written. These tests
/// fail if a second one appears without the guard, so the next author is told where the chokepoint is.
/// </summary>
public sealed class D6SourceTripwireTests
{
    private static string EngineRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var root = Path.Combine(dir.FullName, "sensei-engine", "src");
            if (Directory.Exists(Path.Combine(root, "Sensei.Engine"))) return root;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Sensei.Engine"))) return Path.Combine(dir.FullName, "src");
        }
        throw new DirectoryNotFoundException("sensei-engine/src not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<(string File, string Text)> Sources(string project) =>
        Directory.GetFiles(Path.Combine(EngineRoot(), project), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(p => (Path.GetFileName(p), string.Join("\n", File.ReadAllLines(p).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))));

    [Fact]
    public void D6_the_only_provider_client_is_built_in_the_factory_and_it_installs_the_guard_before_the_transport()
    {
        var builders = Sources("Sensei.Engine").Concat(Sources("Sensei.Engine.Cli"))
            .Where(f => Regex.IsMatch(f.Text, @"new\s+(?:OpenAI\.Chat\.)?ChatClient\s*\(|new\s+OpenAIClient\s*\(|HttpClientPipelineTransport|new\s+HttpClient\s*\("))
            .Select(f => f.File).ToArray();

        Assert.Equal(new[] { "ChatClientFactory.cs" }, builders);
        var factory = Sources("Sensei.Engine").Single(f => f.File == "ChatClientFactory.cs").Text;
        Assert.Contains("AddPolicy(guard, PipelinePosition.BeforeTransport)", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void D6_the_only_file_the_cli_writes_is_the_trace_sink_and_it_cleans_the_final_line()
    {
        var writers = Sources("Sensei.Engine.Cli")
            .Where(f => Regex.IsMatch(f.Text, @"File\.(?:Append|Write|Create|Open)|new\s+(?:StreamWriter|FileStream)\s*\("))
            .Select(f => f.File).ToArray();

        Assert.Equal(new[] { "CliTraceSink.cs" }, writers);
        var sink = Sources("Sensei.Engine.Cli").Single(f => f.File == "CliTraceSink.cs").Text;
        Assert.Matches(@"line\s*=\s*JsonRedaction\.SanitizedJsonLine\(JsonSerializer\.Serialize\(entry,", sink);
        Assert.Matches(@"File\.AppendAllText\([^;]*line", sink);
    }

    [Fact]
    public void D6_the_cli_prints_only_through_the_console_writer_except_the_sanitized_crash_line()
    {
        var printers = Sources("Sensei.Engine.Cli")
            .Where(f => Regex.IsMatch(f.Text, @"Console\.(?:Out|Error)\.Write|Console\.Write"))
            .Select(f => f.File).ToArray();

        Assert.Equal(new[] { "Program.cs" }, printers);
        var program = Sources("Sensei.Engine.Cli").Single(f => f.File == "Program.cs").Text;
        Assert.Single(Regex.Matches(program, @"Console\.Out\.WriteLine"));
        Assert.Contains("secrets.Sanitize(ex.Message)", program, StringComparison.Ordinal);
    }

    [Fact]
    public void D6_no_walker_of_the_final_chokepoints_recurses()
    {
        // The sanitizer, the host-client writer and the in-memory walk are iterative: none of them calls itself.
        var sanitizer = Sources("Sensei.Engine").Single(f => f.File == "JsonRedaction.cs").Text;
        foreach (var method in new[] { "SanitizeJson", "SanitizedValue", "SanitizedArgumentsText", "SanitizedJsonLine", "RebuildJson" })
        {
            var body = MethodBody(sanitizer, method);
            Assert.DoesNotMatch(@"(?<![\w.])" + method + @"\s*\(", body.Substring(body.IndexOf('{')));
        }
        var host = Sources("Sensei.Engine").Single(f => f.File == "SerializedRequestGuard.cs").Text;
        var write = MethodBody(host, "Write");
        Assert.DoesNotMatch(@"(?<![\w.])Write\s*\(\s*(?:items|pair|value)", write.Substring(write.IndexOf('{')));
    }

    /// <summary>The text from the declaration of <paramref name="method"/> to the line that closes its body (indentation-based).</summary>
    private static string MethodBody(string source, string method)
    {
        var lines = source.Split('\n');
        var start = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^\s{4}(?:public|private|internal)[^;=]*\b" + method + @"\s*(?:<[^>]*>)?\s*\("));
        Assert.True(start >= 0, "method not found: " + method);
        var end = start + 1;
        while (end < lines.Length && !Regex.IsMatch(lines[end], @"^\s{4}\}\s*$")) end++;
        return string.Join("\n", lines.Skip(start).Take(end - start + 1));
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Sensei.Engine.Cli;
using Sensei.Engine.Json;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.Cli;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Integration.ReviewFinal2;

/// <summary>D6: the iterative sanitizer that every final chokepoint (wire body, host-client JSON, trace line) is built on.</summary>
public sealed class D6WalkTests
{
    private const string Key = "d6-walk-key-QX42-example";

    private static readonly Func<string, string> Clean = JsonRedaction.Cleaner(new Secrets(new[] { Key }));

    private static (JsonWalkStatus Status, string Output, int Values, bool Changed) Walk(string json, int max = JsonRedaction.MaxFinalValues)
    {
        var status = JsonRedaction.SanitizeJson(Encoding.UTF8.GetBytes(json), Clean, max, out var output, out var values, out var changed);
        return (status, Encoding.UTF8.GetString(output), values, changed);
    }

    private static string Escaped(string text) => string.Concat(text.Select(c => "\\u" + ((int)c).ToString("x4")));

    [Fact]
    public void D6_a_string_value_and_a_property_name_are_cleaned_after_unescaping()
    {
        var spelled = Escaped(Key);
        var (status, output, values, changed) = Walk("{\"" + spelled + "\":\"a " + spelled + " b\",\"plain\":\"" + Key + "\"}");

        Assert.Equal(JsonWalkStatus.Ok, status);
        Assert.True(changed);
        Assert.Equal(3, values);
        Assert.Equal("{\"[redacted]\":\"a [redacted] b\",\"plain\":\"[redacted]\"}", output);
    }

    [Fact]
    public void D6_a_document_with_nothing_to_clean_reports_no_change()
    {
        var (status, _, values, changed) = Walk("{\"a\":[1,2,{\"b\":null}],\"c\":\"text\"}");

        Assert.Equal(JsonWalkStatus.Ok, status);
        Assert.False(changed);
        Assert.Equal(7, values);
    }

    [Fact]
    public void D6_duplicate_names_numbers_and_literals_come_out_as_they_went_in()
    {
        var (_, output, _, _) = Walk("{\"a\":1,\"a\":2,\"n\":[1E+2,-0.0,0.10,12345678901234567890,true,false,null]}");

        Assert.Equal("{\"a\":1,\"a\":2,\"n\":[1E+2,-0.0,0.10,12345678901234567890,true,false,null]}", output);
    }

    [Fact]
    public void D6_a_string_that_holds_json_is_cleaned_as_text_and_never_parsed_again()
    {
        var inner = "{\"k\":\"" + Key + "\",\"list\":[1,2,3]}";
        var (_, output, values, _) = Walk("{\"v\":" + JsonSerializer.Serialize(inner) + "}");

        // Two values, the object and the string: the JSON inside the string is not walked, so its values are not counted either.
        Assert.Equal(2, values);
        Assert.Equal("{\"k\":\"[redacted]\",\"list\":[1,2,3]}", JsonDocument.Parse(output).RootElement.GetProperty("v").GetString());
    }

    [Fact]
    public void D6_a_top_level_scalar_is_a_document()
    {
        var (status, output, values, _) = Walk("\"" + Key + "\"");

        Assert.Equal(JsonWalkStatus.Ok, status);
        Assert.Equal(1, values);
        Assert.Equal("\"[redacted]\"", output);
    }

    [Fact]
    public void D6_the_value_cap_is_exact()
    {
        string Ones(int n) => "[" + string.Join(",", Enumerable.Repeat("1", n - 1)) + "]";

        Assert.Equal(JsonWalkStatus.Ok, Walk(Ones(20_000)).Status);
        Assert.Equal(JsonWalkStatus.TooManyValues, Walk(Ones(20_001)).Status);
        Assert.Equal(JsonWalkStatus.Ok, Walk(Ones(10), 10).Status);
        Assert.Equal(JsonWalkStatus.TooManyValues, Walk(Ones(11), 10).Status);
        Assert.Equal(JsonWalkStatus.TooManyValues, Walk("1", 0).Status);
        Assert.Empty(Walk(Ones(20_001)).Output);
    }

    [Fact]
    public void D6_property_names_are_not_values()
    {
        var names = "{" + string.Join(",", Enumerable.Range(0, 10_000).Select(i => "\"n" + i + "\":1")) + "}";

        var (status, _, values, _) = Walk(names);

        Assert.Equal(JsonWalkStatus.Ok, status);
        Assert.Equal(10_001, values);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tru")]
    [InlineData("{")]
    [InlineData("[1,]")]
    [InlineData("{\"a\":1,}")]
    [InlineData("// c\n1")]
    [InlineData("1 2")]
    [InlineData("{\"a\":1}{\"b\":2}")]
    [InlineData("{\"a\":\"\\ud800\"}")]
    [InlineData("{'a':1}")]
    [InlineData("NaN")]
    public void D6_text_that_is_not_one_json_document_is_refused(string json)
    {
        var (status, output, values, changed) = Walk(json);

        Assert.Equal(JsonWalkStatus.NotJson, status);
        Assert.Empty(output);
        Assert.Equal(0, values);
        Assert.False(changed);
    }

    [Fact]
    public void D6_a_very_deep_document_is_walked_without_recursion()
    {
        var depth = 19_000;
        var json = new string('[', depth) + "\"" + Key + "\"" + new string(']', depth);

        var (status, output, values, changed) = Walk(json);

        Assert.Equal(JsonWalkStatus.Ok, status);
        Assert.True(changed);
        Assert.Equal(depth + 1, values);
        Assert.DoesNotContain(Key, output, StringComparison.Ordinal);
    }

    [Fact]
    public void D6_text_over_depth_that_the_value_cap_forbids_is_refused_not_overflowed()
    {
        var json = new string('[', 25_000) + new string(']', 25_000);

        Assert.Equal(JsonWalkStatus.TooManyValues, Walk(json).Status);
    }

    [Fact]
    public void D6_invalid_utf8_is_refused()
    {
        var status = JsonRedaction.SanitizeJson(new byte[] { (byte)'"', 0xC3, 0x28, (byte)'"' }, Clean, 100, out _, out _, out _);

        Assert.Equal(JsonWalkStatus.NotJson, status);
    }

    [Fact]
    public void D6_a_line_with_nothing_to_clean_comes_back_as_the_same_text()
    {
        var line = "{\"a\":\"b\"}";

        Assert.Same(line, JsonRedaction.SanitizedJsonLine(line, new Secrets(new[] { Key })));
    }

    [Fact]
    public void D6_a_line_that_cannot_be_walked_is_replaced_whole()
    {
        var secrets = new Secrets(new[] { Key });

        var notJson = JsonRedaction.SanitizedJsonLine("{\"a\":\"" + Key, secrets);
        var tooMany = JsonRedaction.SanitizedJsonLine("[" + string.Join(",", Enumerable.Repeat("\"" + Key + "\"", 20_001)) + "]", secrets);

        Assert.Equal("{\"error_code\":\"trace_line_rejected\",\"detail\":\"trace line not written: not_json\"}", notJson);
        Assert.Equal("{\"error_code\":\"trace_line_rejected\",\"detail\":\"trace line not written: too_many_values\"}", tooMany);
    }
}

/// <summary>D6: the CLI trace file and the console are sanitized in the final serialized form.</summary>
public sealed class D6TraceFileTests
{
    private const string Key = "d6-trace-key-QX42-example";

    /// <summary>A key whose characters the trace serializer writes with escapes (<c>+</c>, <c>&amp;</c>).</summary>
    private const string PlusKey = "d6+trace&key/QX42=example";

    private static TraceEntry Entry(string key) => new()
    {
        At = new DateTimeOffset(2026, 10, 28, 3, 0, 0, TimeSpan.Zero),
        SessionId = "s-test",
        TurnId = 1,
        GenerationId = 1,
        Round = 1,
        CallId = "c1 " + key,
        Tool = "inspect_scene " + key,
        Arguments = "{\"" + key + "\":\"" + key + "\"}",
        Status = ToolStatuses.Failure,
        ErrorCode = ToolErrorCodes.InvalidArguments,
        Revision = 3,
        Result = new ToolResult
        {
            CallId = key,
            Tool = key,
            Status = ToolStatuses.Failure,
            ErrorCode = ToolErrorCodes.InvalidArguments,
            Detail = "bad " + key,
            AffectedIds = new List<string> { key },
            Data = JsonDocument.Parse(new JsonObject
            {
                [key] = key,
                ["list"] = new JsonArray(key, 1, null),
                ["uri"] = "https://example.com/" + key,
                ["json_text"] = "{\"k\":\"" + key + "\"}",
            }.ToJsonString()).RootElement.Clone(),
        },
    };

    private static async Task<string[]> WrittenAsync(TraceEntry entry, Secrets secrets)
    {
        var dir = CliRunner.NewTempDirectory();
        var sink = new CliTraceSink(dir, new CliOutput(new StringWriter(), secrets), echo: false);
        sink.Write(entry);
        var path = Assert.Single(Directory.GetFiles(Path.Combine(dir, "traces"), "*.jsonl"));
        return await File.ReadAllLinesAsync(path);
    }

    [Theory]
    [InlineData(Key)]
    [InlineData(PlusKey)]
    public async Task D6_the_trace_file_holds_the_key_in_no_string_property_name_or_json_text(string key)
    {
        var lines = await WrittenAsync(Entry(key), new Secrets(new[] { key }));

        var line = Assert.Single(lines);
        D6Json.AssertAbsent(line, key);
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("c1 [redacted]", doc.RootElement.GetProperty("call_id").GetString());
        Assert.Equal("{\"[redacted]\":\"[redacted]\"}", doc.RootElement.GetProperty("arguments").GetString());
        var data = doc.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal("[redacted]", data.GetProperty("[redacted]").GetString());
        Assert.Equal("{\"k\":\"[redacted]\"}", data.GetProperty("json_text").GetString());
    }

    [Fact]
    public async Task D6_a_key_the_trace_serializer_writes_with_escapes_is_found_after_unescaping()
    {
        // Without the line guard the file holds the key spelled with escapes: a search of the raw text for it finds nothing.
        var raw = JsonSerializer.Serialize(Entry(PlusKey), SenseiJsonContext.Default.TraceEntry);
        Assert.DoesNotContain(PlusKey, raw, StringComparison.Ordinal);
        Assert.Contains(D6Json.Texts(raw), text => text.Contains(PlusKey, StringComparison.Ordinal));

        var lines = await WrittenAsync(Entry(PlusKey), new Secrets(new[] { PlusKey }));

        D6Json.AssertAbsent(Assert.Single(lines), PlusKey);
    }

    [Fact]
    public async Task D6_an_ordinary_trace_line_is_written_exactly_as_it_was_serialized()
    {
        var entry = Entry("plain");
        var expected = JsonSerializer.Serialize(entry, SenseiJsonContext.Default.TraceEntry);

        var lines = await WrittenAsync(entry, new Secrets(new[] { Key }));

        Assert.Equal(expected, Assert.Single(lines));
    }

    [Fact]
    public async Task D6_a_trace_line_over_the_value_cap_is_replaced_and_the_original_is_never_written()
    {
        var entry = Entry(Key);
        entry.Result!.Data = JsonDocument.Parse("[" + string.Join(",", Enumerable.Repeat("\"" + Key + "\"", 20_001)) + "]").RootElement.Clone();

        var lines = await WrittenAsync(entry, new Secrets(new[] { Key }));

        var line = Assert.Single(lines);
        Assert.Equal("{\"error_code\":\"trace_line_rejected\",\"detail\":\"trace line not written: too_many_values\"}", line);
    }

    [Fact]
    public void D6_a_console_line_is_cleaned_as_text_whatever_it_looks_like()
    {
        var writer = new StringWriter();
        var output = new CliOutput(writer, new Secrets(new[] { Key }));

        output.Line("plain " + Key);
        output.Line("{\"k\":\"" + Key + "\",\"" + Key + "\":1}");
        output.Line("multi\nline " + Key);

        Assert.DoesNotContain(Key, writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("{\"k\":\"[redacted]\",\"[redacted]\":1}", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task D6_end_to_end_the_cli_sends_writes_and_prints_none_of_the_key_in_any_shape()
    {
        using var server = new FakeChatServer()
            .EnqueueToolCalls(("c1", "inspect_scene", "{\"" + Key + "\":\"" + Key + "\",\"list\":[\"" + Key + "\"],\"text\":\"{\\\"k\\\":\\\"" + Key + "\\\"}\"}"))
            .EnqueueText("done " + Key);

        var result = await InProcessCli.ScriptAsync(new[] { "hello " + Key }, env: CliRunner.EnvFor(server, Key), extra: new[] { "--trace" });

        Assert.Equal(0, result.Code);
        Assert.Equal(2, server.Requests.Count);
        foreach (var request in server.Requests) D6Json.AssertAbsent(request.Body, Key);
        var path = Assert.Single(Directory.GetFiles(Path.Combine(result.ProfileDir, "traces"), "*.jsonl"));
        var file = await File.ReadAllTextAsync(path);
        foreach (var line in file.Split('\n', StringSplitOptions.RemoveEmptyEntries)) D6Json.AssertAbsent(line, Key);
        Assert.DoesNotContain(Key, result.Text, StringComparison.Ordinal);
        Assert.Equal("Bearer " + Key, server.Requests[0].Authorization);
    }
}

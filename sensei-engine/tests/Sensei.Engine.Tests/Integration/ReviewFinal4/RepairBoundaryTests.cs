using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Trace;
using static Sensei.Engine.Tests.Integration.ReviewFinal4.ReviewFinal4Tests;

namespace Sensei.Engine.Tests.Integration.ReviewFinal4;

/// <summary>
/// Repair tests for final review round 4 (S133). Every value here is invented; no network, no key. The first group is the measured wall:
/// System.Text.Json has no stack guard of its own, so a CLR value that nests past the serializer's own limit must be refused as
/// <c>not_serializable</c>, never recursed into until the process ends.
/// </summary>
public sealed class RepairBoundaryTests
{
    public sealed class Cycle { public Cycle? Self { get; set; } }

    public sealed class Link { public Link? Next { get; set; } }

    public sealed record Holder(string Text, int Count);

    public sealed record ItemsHolder(List<int> Items);

    public sealed record NodeHolder(JsonNode Node);

    public enum Color { Red, Green }

    private static object Cyclic()
    {
        var cycle = new Cycle();
        cycle.Self = cycle;
        return cycle;
    }

    private static object Chain(int nodes)
    {
        var head = new Link();
        var tail = head;
        for (var i = 1; i < nodes; i++)
        {
            tail.Next = new Link();
            tail = tail.Next;
        }
        return head;
    }

    private static JsonNode NestedNode(int depth)
    {
        JsonNode inner = JsonValue.Create(0)!;
        for (var i = 0; i < depth; i++) inner = new JsonArray(inner);
        return inner;
    }

    private static object Case(string kind) => kind switch
    {
        "cycle" => Cyclic(),
        "chain" => Chain(10_000),
        "cycle_in_json_value" => JsonValue.Create(Cyclic())!,
        "cycle_in_list_in_dictionary" => new Dictionary<string, object?> { ["a"] = new List<object?> { 1, Cyclic() } },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // ---- the wall: nothing recurses on the stack past the serializer's own guard ----------------------------------------------------------------

    [Theory]
    [InlineData("cycle")]
    [InlineData("chain")]
    [InlineData("cycle_in_json_value")]
    [InlineData("cycle_in_list_in_dictionary")]
    public async Task A_cyclic_or_10000_deep_clr_result_through_a_host_client_is_not_serializable_and_the_host_survives(string kind)
    {
        var value = Case(kind);
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(ResultMessage(value))))
            .EnqueueText("never reached");
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(chat: chat, trace: trace);

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        var turn = await t.Engine.RunTurnAsync("two");

        AssertRejected(turn, trace, chat.RequestCount, 1);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("chain")]
    [InlineData("cycle_in_json_value")]
    public async Task A_cyclic_or_10000_deep_clr_result_through_the_real_adapter_sends_nothing(string kind)
    {
        using var server = new FakeChatServer().EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "review4-wall-example", "fake-model", server.BaseUrl));
        using var chat = new SeededWireClient(wire, Case(kind));
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(chatClient: chat, trace: trace);

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        var turn = await t.Engine.RunTurnAsync("two");

        AssertRejected(turn, trace, server.Requests.Count, 0);
    }

    private static void AssertRejected(TurnResult turn, RecordingTraceSink trace, int requests, int expectedRequests)
    {
        Assert.Equal(TurnStatuses.Failed, turn.Status);
        Assert.Equal(TurnErrorCodes.ProviderError, turn.ErrorCode);
        Assert.Equal("outbound request rejected: not_serializable", turn.ErrorDetail);
        Assert.Equal(expectedRequests, requests);
        var entry = Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
        Assert.Equal("engine:outbound_rejected", entry.Tool);
        Assert.Equal("request not sent: not_serializable", entry.Arguments);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("chain")]
    [InlineData("cycle_in_json_value")]
    [InlineData("cycle_in_list_in_dictionary")]
    public void The_in_memory_sanitizer_and_the_host_guard_each_refuse_a_value_nested_past_the_serializer_guard(string kind)
    {
        var value = Case(kind);

        var rejected = Assert.Throws<OutboundRejectedException>(() =>
            OutboundMessages.Sanitized(new[] { ResultMessage(value) }, Secrets.None, new List<string>()));
        Assert.Equal("not_serializable", rejected.Reason);
        Assert.Null(rejected.InnerException);

        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
        Assert.Equal("not_serializable", Assert.Throws<OutboundRejectedException>(() => guard.Guarded(new List<ChatMessage> { ResultMessage(value) })).Reason);

        var arguments = new FunctionCallContent("c", "inspect_scene", new Dictionary<string, object?> { ["x"] = value });
        var inArguments = new ChatMessage(ChatRole.Assistant, new List<AIContent> { arguments });
        Assert.Equal("not_serializable", Assert.Throws<OutboundRejectedException>(() =>
            OutboundMessages.Sanitized(new[] { inArguments }, Secrets.None, new List<string>())).Reason);
    }

    [Fact]
    public void The_serializer_nesting_guard_stays_the_framework_default()
    {
        // Raising it to the value cap turns a cyclic object into a stack overflow: see JsonRedaction.ClrNestingLimit.
        Assert.Equal(64, JsonRedaction.ClrNestingLimit);
    }

    /// <summary>Runs <paramref name="action"/> on a thread with a 1 MiB stack, the smallest a host is likely to give, and rethrows what it threw.</summary>
    private static void OnSmallStack(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }, 1024 * 1024);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData(1_500, "kept")]
    [InlineData(9_000, "kept")]
    [InlineData(12_000, "oversized")]
    [InlineData(25_000, "rejected")]
    public void A_record_holding_a_deep_json_node_is_written_without_recursion(int depth, string outcome)
    {
        // A node's own WriteTo recurses once per level; measured: 20 000 levels end the process on a 1 MiB stack. The walk is iterative:
        // the count decides (root + 9 999 kept, more replaced), and a node over the request cap is refused when it is written.
        var holder = new NodeHolder(NestedNode(depth));

        OnSmallStack(() =>
        {
            if (outcome == "rejected")
            {
                var rejected = Assert.Throws<OutboundRejectedException>(() =>
                    OutboundMessages.Sanitized(new[] { ResultMessage(holder) }, Secrets.None, new List<string>()));
                Assert.Equal("not_serializable", rejected.Reason);
                return;
            }
            var clean = JsonRedaction.SanitizedValue(holder, Secrets.None, out var oversized);
            Assert.Equal(outcome == "oversized", oversized);
            if (!oversized) Assert.Equal(depth + 2, ValueCount(Assert.IsType<JsonElement>(clean).GetRawText()));
        });
    }

    [Theory]
    [InlineData(12_000)]
    [InlineData(25_000)]
    public void A_deep_node_in_a_record_in_a_json_value_is_written_without_recursion(int depth)
    {
        var node = JsonValue.Create(new NodeHolder(NestedNode(depth)))!;

        OnSmallStack(() =>
        {
            if (depth > 20_000)
            {
                Assert.ThrowsAny<Exception>(() => JsonRedaction.SanitizedValue(node, Secrets.None, out _));
                return;
            }
            JsonRedaction.SanitizedValue(node, Secrets.None, out var oversized);
            Assert.True(oversized);
        });
    }


    // ---- CLR values are read inside the boundary -------------------------------------------------------------------------------------------

    [Fact]
    public void A_boolean_or_a_finite_number_goes_on_as_it_is()
    {
        foreach (var scalar in new object[] { true, (byte)7, 5, 5L, 5UL, 1.5f, 1.5, 1.5m })
            Assert.Same(scalar, JsonRedaction.SanitizedValue(scalar, Secrets.None));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_number_that_is_not_finite_is_not_serializable(double number)
    {
        var rejected = Assert.Throws<OutboundRejectedException>(() =>
            OutboundMessages.Sanitized(new[] { ResultMessage(number) }, Secrets.None, new List<string>()));
        Assert.Equal("not_serializable", rejected.Reason);
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
        Assert.Equal("not_serializable", Assert.Throws<OutboundRejectedException>(() => guard.Guarded(new List<ChatMessage> { ResultMessage(number) })).Reason);
    }

    [Fact]
    public void A_record_a_uri_a_guid_and_an_enum_leave_as_detached_json_elements_with_the_secret_removed()
    {
        const string secret = "review4-record-ZX42";
        var secrets = new Secrets(new[] { secret });
        var guid = Guid.Parse("00000000-0000-0000-0000-00000000002a");
        var message = ResultMessage(new List<object?>
        {
            new Holder("note " + secret, 3), new Uri("https://example.com/" + secret), guid, Color.Green, new Dictionary<string, int> { [secret] = 1 },
        });

        var result = OutboundMessages.Sanitized(new[] { message }, secrets, new List<string>()).Single().Contents.OfType<FunctionResultContent>().Single().Result;

        var list = Assert.IsType<List<object?>>(result);
        // A dictionary that is not string-to-object is walked as a sequence, as before: each pair is a record written under its runtime type.
        var pairs = Assert.IsType<List<object?>>(list[4]);
        var flat = list.Take(4).Concat(pairs).ToList();
        Assert.All(flat, item => Assert.IsType<JsonElement>(item));
        var text = string.Join("|", flat.Select(item => ((JsonElement)item!).GetRawText()));
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.String, ((JsonElement)list[2]!).ValueKind);
        Assert.Equal(3, ((JsonElement)list[0]!).EnumerateObject().Single(p => p.NameEquals("count")).Value.GetInt32());
    }

    [Fact]
    public void A_getter_that_throws_inside_a_nested_value_is_not_serializable_with_no_foreign_text()
    {
        var value = new Dictionary<string, object?> { ["a"] = new List<object?> { 1, new ThrowingValue() } };
        var call = new FunctionCallContent("c", "inspect_scene", value);

        var rejected = Assert.Throws<OutboundRejectedException>(() =>
            OutboundMessages.Sanitized(new[] { new ChatMessage(ChatRole.Assistant, new List<AIContent> { call }) }, Secrets.None, new List<string>()));

        Assert.Equal("not_serializable", rejected.Reason);
        Assert.Null(rejected.InnerException);
        Assert.DoesNotContain("foreign", rejected.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(9_998, false)]
    [InlineData(9_999, true)]
    public void A_clr_value_shares_the_node_counter_with_what_it_is_written_as(int items, bool replaced)
    {
        // The record is the root (1), its list (1) and the numbers: root + 9 999 kept, root + 10 000 replaced whole, as for any other value.
        var holder = new ItemsHolder(Enumerable.Range(0, items).ToList());
        var notes = new List<string>();

        var result = OutboundMessages.Sanitized(new[] { ResultMessage(holder) }, Secrets.None, notes).Single().Contents.OfType<FunctionResultContent>().Single().Result;

        if (replaced)
        {
            Assert.Equal("[oversized value]", result);
            Assert.Single(notes);
        }
        else
        {
            Assert.IsType<JsonElement>(result);
            Assert.Empty(notes);
        }
    }

    [Fact]
    public void A_clr_value_under_a_deep_container_is_not_judged_by_how_deep_the_container_is()
    {
        // The leaf is written into a writer of its own, so the serializer counts the leaf's own nesting, not the 100 levels above it.
        object value = new Uri("https://example.com/leaf");
        for (var i = 0; i < 100; i++) value = new Dictionary<string, object?> { ["k"] = value };
        var messages = new List<ChatMessage> { ResultMessage(value) };
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);

        Assert.Same(messages, guard.Guarded(messages));
    }

    [Fact]
    public void A_json_array_of_scalar_and_clr_leaves_is_walked_and_counted_value_by_value()
    {
        var array = new JsonArray(JsonValue.Create(5), JsonValue.Create(true), JsonValue.Create(Guid.Empty), JsonValue.Create(new Uri("https://example.com/a")),
            JsonValue.Create(new Holder("x", 1)));

        var clean = Assert.IsType<JsonElement>(JsonRedaction.SanitizedValue(array, Secrets.None, out var oversized));

        Assert.False(oversized);
        Assert.Equal(5, clean.GetArrayLength());
        Assert.Equal(5, clean[0].GetInt32());
        Assert.True(clean[1].GetBoolean());
        Assert.Equal(1 + 1 + 1 + 1 + 1 + 3, ValueCount(clean.GetRawText())); // the array, four leaves, the record and its two properties
    }

    [Fact]
    public void A_deep_element_held_by_a_record_is_counted_before_it_is_judged()
    {
        // 12 000 levels: more than the in-memory cap, less than the host cap. The count decides, not a depth error.
        var holder = new WrappedJson(WrappedNode(12_000).GetValue<WrappedJson>().Value);
        var record = JsonValue.Create(holder)!;

        JsonRedaction.SanitizedValue(holder, Secrets.None, out var oversizedRecord);
        JsonRedaction.SanitizedValue(record, Secrets.None, out var oversizedValue);
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
        var messages = new List<ChatMessage> { ResultMessage(holder) };

        Assert.True(oversizedRecord);
        Assert.True(oversizedValue);
        Assert.Same(messages, guard.Guarded(messages));
    }

    // ---- the tripwire: no JSON call on an outbound path without a depth bound -------------------------------------------------------------------

    private static readonly string[] OutboundFolders = { "Trace", "Llm", "Agent" };

    private static IEnumerable<(string Name, string Text)> OutboundSources()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var root = Path.Combine(dir.FullName, "sensei-engine", "src", "Sensei.Engine");
            if (!Directory.Exists(root)) root = Path.Combine(dir.FullName, "src", "Sensei.Engine");
            if (!Directory.Exists(root)) continue;
            var separator = Path.DirectorySeparatorChar;
            return OutboundFolders.SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories)
                    .Where(p => !p.Contains(separator + "obj" + separator) && !p.Contains(separator + "bin" + separator))
                    .Select(p => (folder + "/" + Path.GetFileName(p),
                        string.Join("\n", File.ReadAllLines(p).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))))))
                .ToList();
        }
        throw new DirectoryNotFoundException("sensei-engine/src not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The text from <paramref name="open"/> (an opening bracket) to its closing one, string literals skipped.</summary>
    private static string Balanced(string text, int open, char opening, char closing)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                for (i++; i < text.Length && text[i] != '"'; i++)
                    if (text[i] == '\\') i++;
                continue;
            }
            if (c == opening) depth++;
            else if (c == closing && --depth == 0) return text.Substring(open, i - open + 1);
        }
        return text.Substring(open);
    }

    /// <summary>What a scan of one source file finds wrong, and how many JSON calls and option constructions it looked at.</summary>
    internal static (List<string> Violations, int Checked) Scan(string source)
    {
        var violations = new List<string>();
        var checkedCount = 0;

        // Every call that reads or writes JSON in memory carries a MaxDepth (an option named for it counts).
        foreach (Match m in Regex.Matches(source, @"new Utf8JsonWriter\(|new Utf8JsonReader\(|JsonDocument\.Parse(Async)?\(|JsonNode\.Parse\(|JsonElement\.ParseValue\(|\.ToJsonString\("))
        {
            checkedCount++;
            var call = Balanced(source, m.Index + m.Length - 1, '(', ')');
            if (!call.Contains("MaxDepth", StringComparison.Ordinal)) violations.Add("no MaxDepth: " + m.Value + call);
        }

        // Every options object is built with one, explicitly typed so that this scan sees it.
        foreach (Match m in Regex.Matches(source, @"new (JsonWriterOptions|JsonReaderOptions|JsonDocumentOptions|JsonSerializerOptions)\b"))
        {
            checkedCount++;
            var end = m.Index + m.Length;
            while (end < source.Length && char.IsWhiteSpace(source[end])) end++;
            var window = m.Value;
            if (end < source.Length && source[end] == '(')
            {
                var arguments = Balanced(source, end, '(', ')');
                window += arguments;
                end += arguments.Length;
                while (end < source.Length && char.IsWhiteSpace(source[end])) end++;
            }
            if (end < source.Length && source[end] == '{') window += Balanced(source, end, '{', '}');
            if (!window.Contains("MaxDepth", StringComparison.Ordinal)) violations.Add("options without MaxDepth: " + window);
        }
        foreach (Match m in Regex.Matches(source, @"Json(Writer|Reader|Document|Serializer)Options\??\s+\w+\s*=\s*new\("))
            violations.Add("target-typed options (spell the type so MaxDepth can be checked): " + m.Value);

        // The serializer is called only to write into a writer of the caller's own, never to a string, bytes, element or node (those
        // build the writer from the options and hide the depth); a typed engine record through the generated context is fixed-shape.
        foreach (Match m in Regex.Matches(source, @"JsonSerializer\.(\w+)(<[^>(]*>)?\("))
        {
            checkedCount++;
            var call = Balanced(source, m.Index + m.Length - 1, '(', ')');
            if (call.Contains("SenseiJsonContext.Default.", StringComparison.Ordinal)) continue;
            var writesToWriter = m.Groups[1].Value == "Serialize" && Regex.IsMatch(call, @"^\(\s*writer\s*,");
            if (!writesToWriter) violations.Add("serializer call that is not a write into a caller's writer: " + m.Value + call);
        }
        return (violations, checkedCount);
    }

    [Fact]
    public void T01_every_json_call_on_an_outbound_path_carries_a_max_depth()
    {
        var sources = OutboundSources().ToList();
        var violations = new List<string>();
        var total = 0;
        foreach (var (name, text) in sources)
        {
            var (found, count) = Scan(text);
            total += count;
            violations.AddRange(found.Select(v => name + ": " + v));
        }

        Assert.Empty(violations);
        // Vacuity guard: all three folders were read and the scan really saw the calls (the walkers, the guard, the Agent parses).
        Assert.All(OutboundFolders, folder => Assert.Contains(sources, s => s.Name.StartsWith(folder + "/", StringComparison.Ordinal)));
        Assert.True(total >= 20, "expected at least 20 JSON calls and option constructions, saw " + total);
    }

    [Theory]
    [InlineData("var w = new Utf8JsonWriter(stream);", "no MaxDepth")]
    [InlineData("var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });", "options without MaxDepth")]
    [InlineData("var r = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });", "options without MaxDepth")]
    [InlineData("using var d = JsonDocument.Parse(\"{}\");", "no MaxDepth")]
    [InlineData("var n = JsonNode.Parse(text);", "no MaxDepth")]
    [InlineData("var t = node.ToJsonString();", "no MaxDepth")]
    [InlineData("var t = node.ToJsonString(Options);", "no MaxDepth")]
    [InlineData("var o = new JsonSerializerOptions { WriteIndented = true };", "options without MaxDepth")]
    [InlineData("var o = new JsonSerializerOptions(other);", "options without MaxDepth")]
    [InlineData("JsonDocumentOptions o = new() { MaxDepth = 5 };", "target-typed options")]
    [InlineData("var s = JsonSerializer.Serialize(value);", "serializer call")]
    [InlineData("var s = JsonSerializer.Serialize(value, info);", "serializer call")]
    [InlineData("var s = JsonSerializer.SerializeToUtf8Bytes(value, info);", "serializer call")]
    [InlineData("var s = JsonSerializer.SerializeToElement(value, info);", "serializer call")]
    [InlineData("var s = JsonSerializer.Deserialize(text, info);", "serializer call")]
    public void T02_the_tripwire_fails_on_a_call_without_a_depth_bound(string source, string expected)
    {
        var (violations, _) = Scan(source);

        Assert.Contains(violations, v => v.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = e, MaxDepth = depth });")]
    [InlineData("var r = new Utf8JsonReader(json, new JsonReaderOptions\n{\n    AllowTrailingCommas = false,\n    MaxDepth = depth,\n});")]
    [InlineData("using var d = JsonDocument.Parse(\"{\\\"type\\\":\\\"object\\\"}\", new JsonDocumentOptions { MaxDepth = 20_001 });")]
    [InlineData("var t = node.ToJsonString(ScalarMaxDepthOptions);")]
    [InlineData("var o = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { MaxDepth = 64 };")]
    [InlineData("JsonSerializer.Serialize(writer, value, info);")]
    [InlineData("var s = JsonSerializer.Serialize(snapshot, SenseiJsonContext.Default.SceneSnapshot);")]
    [InlineData("return JsonSerializer.SerializeToElement(result, SenseiJsonContext.Default.ToolResult);")]
    public void T03_the_tripwire_passes_a_bounded_call(string source)
    {
        var (violations, checkedCount) = Scan(source);

        Assert.Empty(violations);
        Assert.True(checkedCount >= 1);
    }
}

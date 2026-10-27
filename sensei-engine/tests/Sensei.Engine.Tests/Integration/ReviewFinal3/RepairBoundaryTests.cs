using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Trace;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Integration.ReviewFinal3;

/// <summary>
/// The edges of the repair for the third final review: the request-wide value budget (text and instructions), the iterative walk of JSON
/// elements and nodes in the host guard and the in-memory sanitizer, and the one place an unwritable value turns into a rejection.
/// </summary>
public sealed class RepairBoundaryTests
{
    private const string Key = "final3-repair-key-QX42-example";

    private static readonly Secrets Known = new(new[] { Key });

    private static JsonElement DeepElement(int depth, string leaf = "0") => JsonDocument.Parse(
        new string('[', depth) + leaf + new string(']', depth),
        new JsonDocumentOptions { MaxDepth = 30_001 }).RootElement.Clone();

    private static JsonNode DeepNode(int depth, string leaf = "0") => JsonNode.Parse(
        new string('[', depth) + leaf + new string(']', depth),
        documentOptions: new JsonDocumentOptions { MaxDepth = 30_001 })!;

    private static ChatMessage TextMessage(int texts) => new(ChatRole.User, Enumerable.Range(0, texts).Select(_ => (AIContent)new TextContent("hello")).ToList());

    private static ChatMessage ResultMessage(object? value) => new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("r", value) });

    private static async Task<Exception?> Send(SerializedRequestGuard guard, string entry, List<ChatMessage> messages, ChatOptions? options = null)
    {
        return await Record.ExceptionAsync(async () =>
        {
            switch (entry)
            {
                case "response":
                    await guard.GetResponseAsync(messages, options);
                    break;
                case "streaming":
                    await foreach (var _ in guard.GetStreamingResponseAsync(messages, options)) { }
                    break;
                default:
                    guard.Guarded(messages);
                    break;
            }
        });
    }

    // ---- F03: the request-wide budget -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("guarded", 20_000, false)]
    [InlineData("guarded", 20_001, true)]
    [InlineData("response", 20_000, false)]
    [InlineData("response", 20_001, true)]
    [InlineData("streaming", 20_000, false)]
    [InlineData("streaming", 20_001, true)]
    public async Task F03_text_values_are_accepted_up_to_exactly_the_cap_and_refused_one_above(string entry, int texts, bool refused)
    {
        var inner = new ScriptedChatClient().EnqueueText("done");
        using var guard = new SerializedRequestGuard(inner, Secrets.None);

        var error = await Send(guard, entry, new List<ChatMessage> { TextMessage(texts) });

        if (refused)
        {
            Assert.Equal("too_many_values", Assert.IsType<OutboundRejectedException>(error).Reason);
            Assert.Equal(0, inner.RequestCount);
        }
        else
        {
            Assert.Null(error);
            Assert.Equal(entry == "guarded" ? 0 : 1, inner.RequestCount);
        }
    }

    [Theory]
    [InlineData("response", 19_999, false)]
    [InlineData("response", 20_000, true)]
    [InlineData("streaming", 19_999, false)]
    [InlineData("streaming", 20_000, true)]
    public async Task F03_the_instructions_are_one_more_value_of_the_same_budget(string entry, int texts, bool refused)
    {
        var inner = new ScriptedChatClient().EnqueueText("done");
        using var guard = new SerializedRequestGuard(inner, Secrets.None);

        var error = await Send(guard, entry, new List<ChatMessage> { TextMessage(texts) }, new ChatOptions { Instructions = "Be kind." });

        if (refused)
        {
            Assert.Equal("too_many_values", Assert.IsType<OutboundRejectedException>(error).Reason);
            Assert.Equal(0, inner.RequestCount);
        }
        else
        {
            Assert.Null(error);
            Assert.Equal(1, inner.RequestCount);
        }
    }

    [Fact]
    public async Task F03_blank_instructions_are_not_a_value_and_do_not_use_the_budget()
    {
        var inner = new ScriptedChatClient().EnqueueText("done");
        using var guard = new SerializedRequestGuard(inner, Secrets.None);

        var error = await Send(guard, "response", new List<ChatMessage> { TextMessage(20_000) }, new ChatOptions { Instructions = "" });

        Assert.Null(error);
        Assert.Equal(1, inner.RequestCount);
    }

    [Theory]
    [InlineData(9_999, false)]
    [InlineData(10_000, true)]
    public async Task F03_text_and_structured_values_draw_on_one_budget(int texts, bool refused)
    {
        // 10,000 numbers in an array are 10,001 values; the texts make up the rest of the 20,000.
        var array = Enumerable.Repeat<object?>(1, 10_000).ToList();
        var messages = new List<ChatMessage> { TextMessage(texts), ResultMessage(array) };
        var inner = new ScriptedChatClient().EnqueueText("done");
        using var guard = new SerializedRequestGuard(inner, Secrets.None);

        var error = await Send(guard, "response", messages);

        if (refused) Assert.Equal("too_many_values", Assert.IsType<OutboundRejectedException>(error).Reason);
        else Assert.Null(error);
        Assert.Equal(refused ? 0 : 1, inner.RequestCount);
    }

    [Fact]
    public async Task F03_text_that_needs_cleaning_is_counted_once_and_still_cleaned()
    {
        var inner = new ScriptedChatClient().EnqueueText("done");
        using var guard = new SerializedRequestGuard(inner, Known);
        var messages = new List<ChatMessage> { new(ChatRole.User, "see " + Key) };

        await guard.GetResponseAsync(messages);

        var sent = Assert.IsType<TextContent>(Assert.Single(Assert.Single(inner.Requests).Messages).Contents.Single());
        Assert.Equal("see [redacted]", sent.Text);
    }

    // ---- F01: elements and nodes are walked, not printed ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("element", 64)]
    [InlineData("element", 65)]
    [InlineData("element", 5_000)]
    [InlineData("element", 19_999)]
    [InlineData("node", 64)]
    [InlineData("node", 65)]
    [InlineData("node", 5_000)]
    [InlineData("node", 19_999)]
    public void F01_a_deep_value_under_the_cap_is_passed_on_as_the_same_messages(string shape, int depth)
    {
        object value = shape == "element" ? DeepElement(depth) : DeepNode(depth);
        var messages = new List<ChatMessage> { ResultMessage(value) };
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);

        Assert.Same(messages, guard.Guarded(messages));
    }

    [Theory]
    [InlineData("element", 20_000)]
    [InlineData("element", 25_000)]
    [InlineData("node", 20_000)]
    [InlineData("node", 25_000)]
    public void F01_a_deep_value_over_the_cap_is_refused_by_count_not_by_a_depth_limit(string shape, int depth)
    {
        // depth arrays plus one number: depth + 1 values, over the 20,000 of the request.
        object value = shape == "element" ? DeepElement(depth) : DeepNode(depth);
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);

        var error = Assert.Throws<OutboundRejectedException>(() => guard.Guarded(new List<ChatMessage> { ResultMessage(value) }));

        Assert.Equal("too_many_values", error.Reason);
    }

    [Theory]
    [InlineData("element")]
    [InlineData("node")]
    public void F01_a_secret_at_the_bottom_of_a_deep_value_is_cleaned(string shape)
    {
        object value = shape == "element" ? DeepElement(2_000, "\"a " + Key + "\"") : DeepNode(2_000, "\"a " + Key + "\"");
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Known);

        var guarded = guard.Guarded(new List<ChatMessage> { ResultMessage(value) });

        var sent = Assert.IsType<JsonElement>(guarded.Single().Contents.OfType<FunctionResultContent>().Single().Result);
        var text = sent.GetRawText();
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        Assert.EndsWith("\"a [redacted]\"" + new string(']', 2_000), text, StringComparison.Ordinal);
    }

    [Fact]
    public void F01_nodes_that_hold_other_values_are_written_and_cleaned_inside_an_object_argument()
    {
        // A node can hold a scalar element (the framework refuses an object or an array element).
        var element = JsonDocument.Parse("\"x " + Key + "\"").RootElement.Clone();
        var node = new JsonObject
        {
            ["number"] = JsonValue.Create(5),
            ["flag"] = JsonValue.Create(true),
            ["text"] = JsonValue.Create("t " + Key),
            ["uri"] = JsonValue.Create(new Uri("https://example.com/" + Key)),
            ["element"] = JsonValue.Create(element),
            [Key] = null,
            ["list"] = new JsonArray(JsonValue.Create(1), null, new JsonObject { ["deep"] = JsonValue.Create("d " + Key) }),
        };
        var call = new FunctionCallContent("c", "inspect_scene", new Dictionary<string, object?> { ["extra"] = node });
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Known);

        var guarded = guard.Guarded(new List<ChatMessage> { new(ChatRole.Assistant, new List<AIContent> { call }) });

        var sent = Assert.IsType<JsonElement>(guarded.Single().Contents.OfType<FunctionCallContent>().Single().Arguments!["extra"]);
        // The credential pattern (S78) decides how much of the address goes with the key, so that one value is only checked for the key.
        var uri = sent.GetProperty("uri").GetString()!;
        Assert.Contains("[redacted]", uri, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, uri, StringComparison.Ordinal);
        Assert.Equal(
            "{\"number\":5,\"flag\":true,\"text\":\"t [redacted]\",\"uri\":" + JsonSerializer.Serialize(uri) + ",\"element\":\"x [redacted]\",\"[redacted]\":null,\"list\":[1,null,{\"deep\":\"d [redacted]\"}]}",
            sent.GetRawText());
    }

    // ---- F02: the in-memory walk -----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(9_999, false)]
    [InlineData(10_000, true)]
    public void F02_a_node_of_values_of_other_types_is_counted_value_by_value(int items, bool replaced)
    {
        var node = new JsonArray();
        for (var i = 0; i < items; i++) node.Add(JsonValue.Create(i));

        var result = JsonRedaction.SanitizedValue(node, Known, out var oversized);

        Assert.Equal(replaced, oversized);
        if (replaced) Assert.Equal("[oversized value]", result);
        else Assert.Equal(items, Assert.IsType<JsonElement>(result).GetArrayLength());
    }

    [Theory]
    [InlineData("element")]
    [InlineData("node")]
    public void F02_a_value_nested_nine_thousand_levels_deep_is_cleaned_without_a_depth_limit(string shape)
    {
        object value = shape == "element" ? DeepElement(9_000, "\"" + Key + "\"") : DeepNode(9_000, "\"" + Key + "\"");

        var result = JsonRedaction.SanitizedValue(value, Known, out var oversized);

        Assert.False(oversized);
        var text = Assert.IsType<JsonElement>(result).GetRawText();
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void F02_a_deep_node_and_a_clr_list_share_one_count()
    {
        // 5,000 values in the node, 5,000 in the list, and the dictionary holding them: 10,002 in all.
        var value = new Dictionary<string, object?>
        {
            ["node"] = DeepNode(1_000, string.Join(',', Enumerable.Repeat("0", 4_000))),
            ["list"] = Enumerable.Repeat<object?>("x", 5_000).ToList(),
        };

        JsonRedaction.SanitizedValue(value, Known, out var oversized);

        Assert.True(oversized);
    }

    [Fact]
    public void F02_a_node_with_a_secret_in_a_name_or_a_string_is_cleaned_everywhere()
    {
        var node = JsonNode.Parse("{\"" + Key + "\":[\"a " + Key + "\",{\"b\":\"" + Key + "\"}]}")!;

        var result = JsonRedaction.SanitizedValue(node, Known, out var oversized);

        Assert.False(oversized);
        Assert.Equal("{\"[redacted]\":[\"a [redacted]\",{\"b\":\"[redacted]\"}]}", Assert.IsType<JsonElement>(result).GetRawText());
    }

    // ---- F04: one boundary ---------------------------------------------------------------------------------------------------------------------

    private const string ForeignText = "foreign-exception-text-7731";

    private sealed class BrokenEnumerable : IEnumerable
    {
        public IEnumerator GetEnumerator() => throw new InvalidOperationException(ForeignText);
    }

    private static JsonElement Disposed()
    {
        var doc = JsonDocument.Parse("{\"a\":[1,2]}");
        var element = doc.RootElement;
        doc.Dispose();
        return element;
    }

    public static IEnumerable<object[]> UnwritableValues()
    {
        yield return new object[] { "disposed element", (Func<object>)(() => Disposed()) };
        yield return new object[] { "disposed element in a list", (Func<object>)(() => new List<object?> { 1, new List<object?> { Disposed() } }) };
        yield return new object[] { "disposed element in a dictionary", (Func<object>)(() => new Dictionary<string, object?> { ["k"] = Disposed() }) };
        yield return new object[] { "undefined element", (Func<object>)(() => default(JsonElement)) };
        yield return new object[] { "throwing enumerable", (Func<object>)(() => new BrokenEnumerable()) };
        yield return new object[] { "throwing enumerable in a list", (Func<object>)(() => new List<object?> { "ok", new BrokenEnumerable() }) };
    }

    [Theory]
    [MemberData(nameof(UnwritableValues))]
    public void F04_the_in_memory_sanitizer_turns_an_unwritable_result_into_the_engines_own_rejection(string name, Func<object> make)
    {
        _ = name;
        var message = ResultMessage(make());

        var error = Assert.Throws<OutboundRejectedException>(() => OutboundMessages.Sanitized(new[] { message }, Known, new List<string>()));

        Assert.Equal("not_serializable", error.Reason);
        Assert.Equal("outbound request rejected: not_serializable", error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [MemberData(nameof(UnwritableValues))]
    public void F04_the_in_memory_sanitizer_turns_an_unwritable_call_argument_into_the_engines_own_rejection(string name, Func<object> make)
    {
        _ = name;
        var call = new FunctionCallContent("c", "inspect_scene", new Dictionary<string, object?> { ["extra"] = make() });
        var message = new ChatMessage(ChatRole.Assistant, new List<AIContent> { call });

        var error = Assert.Throws<OutboundRejectedException>(() => OutboundMessages.Sanitized(new[] { message }, Known, new List<string>()));

        Assert.Equal("not_serializable", error.Reason);
    }

    [Fact]
    public void F04_the_in_memory_sanitizer_rejects_a_dictionary_of_arguments_that_cannot_be_copied()
    {
        var call = new FunctionCallContent("c", "inspect_scene", new ThrowingArguments());
        var message = new ChatMessage(ChatRole.Assistant, new List<AIContent> { call });

        var error = Assert.Throws<OutboundRejectedException>(() => OutboundMessages.Sanitized(new[] { message }, Known, new List<string>()));

        Assert.Equal("not_serializable", error.Reason);
    }

    [Theory]
    [MemberData(nameof(UnwritableValues))]
    public void F04_the_host_guard_alone_rejects_the_same_values_the_same_way(string name, Func<object> make)
    {
        _ = name;
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Known);

        var error = Assert.Throws<OutboundRejectedException>(() => guard.Guarded(new List<ChatMessage> { ResultMessage(make()) }));

        Assert.Equal("not_serializable", error.Reason);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task F04_a_rejected_turn_carries_none_of_the_foreign_exception_text()
    {
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                new List<AIContent> { new TextContent("answer"), new FunctionResultContent("prior", new BrokenEnumerable()) }))))
            .EnqueueText("never reached");
        var trace = new RecordingTraceSink();
        await using var t = await EngineAsync(chat, trace: trace);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);

        var turn = await t.Engine.RunTurnAsync("two");

        Assert.Equal("outbound request rejected: not_serializable", turn.ErrorDetail);
        var rejection = Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
        Assert.Equal("request not sent: not_serializable", rejection.Arguments);
        Assert.DoesNotContain(trace.Entries, e => e.Arguments.Contains(ForeignText, StringComparison.Ordinal)
            || (e.Result?.Detail ?? "").Contains(ForeignText, StringComparison.Ordinal));
        Assert.Equal(1, chat.RequestCount);
    }

    private sealed class ThrowingArguments : IDictionary<string, object?>
    {
        public object? this[string key] { get => throw new InvalidOperationException("x"); set => throw new InvalidOperationException("x"); }
        public ICollection<string> Keys => throw new InvalidOperationException("x");
        public ICollection<object?> Values => throw new InvalidOperationException("x");
        public int Count => 1;
        public bool IsReadOnly => true;
        public void Add(string key, object? value) => throw new InvalidOperationException("x");
        public void Add(KeyValuePair<string, object?> item) => throw new InvalidOperationException("x");
        public void Clear() => throw new InvalidOperationException("x");
        public bool Contains(KeyValuePair<string, object?> item) => throw new InvalidOperationException("x");
        public bool ContainsKey(string key) => throw new InvalidOperationException("x");
        public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) => throw new InvalidOperationException("x");
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => throw new InvalidOperationException("x");
        public bool Remove(string key) => throw new InvalidOperationException("x");
        public bool Remove(KeyValuePair<string, object?> item) => throw new InvalidOperationException("x");
        public bool TryGetValue(string key, out object? value) => throw new InvalidOperationException("x");
        IEnumerator IEnumerable.GetEnumerator() => throw new InvalidOperationException("x");
    }

    // ---- the shape of the code ---------------------------------------------------------------------------------------------------------------------

    private static string Source(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var root = Path.Combine(dir.FullName, "sensei-engine", "src", "Sensei.Engine");
            if (!Directory.Exists(root)) root = Path.Combine(dir.FullName, "src", "Sensei.Engine");
            if (!Directory.Exists(root)) continue;
            var path = Directory.GetFiles(root, file, SearchOption.AllDirectories).Single(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
            return string.Join("\n", File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        }
        throw new DirectoryNotFoundException("sensei-engine/src not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void F01_the_host_guard_no_longer_prints_an_element_or_a_node_to_text_and_writes_that()
    {
        var guard = Source("SerializedRequestGuard.cs");

        Assert.DoesNotContain("GetRawText(", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("ToJsonString(", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteRawValue(", guard, StringComparison.Ordinal);
        Assert.Contains("JsonRedaction.WriteJsonTree(", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void F02_the_in_memory_sanitizer_walks_an_element_or_a_node_and_never_replaces_one_it_could_not_read()
    {
        var redaction = Source("JsonRedaction.cs");

        // Printing to text is only the way in for a leaf node that holds a value of another type (WriteTreeLeaf) and for a number.
        Assert.Single(Regex.Matches(redaction, @"ToJsonString\("));
        Assert.Single(Regex.Matches(redaction, @"GetRawText\("));
        Assert.DoesNotMatch(@"catch\s*(\([^)]*\))?\s*\{[^}]*OversizedValue", redaction);
    }

    [Fact]
    public void F04_only_the_outbound_preparation_boundaries_catch_what_an_unwritable_value_throws()
    {
        var messages = Source("OutboundMessages.cs");
        var redaction = Source("JsonRedaction.cs");

        Assert.Single(Regex.Matches(messages, @"catch\s*\(Exception\)"));
        Assert.Contains("OutboundRejectedException.NotSerializable", messages, StringComparison.Ordinal);
        // The walk itself catches only inside the byte parser (the reader and its trace/argument callers), never in the tree walk.
        var tree = redaction.Substring(redaction.IndexOf("internal static bool WriteJsonTree", StringComparison.Ordinal));
        tree = tree.Substring(0, tree.IndexOf("private static bool WriteTreeValue", StringComparison.Ordinal));
        Assert.DoesNotContain("catch", tree, StringComparison.Ordinal);
    }
}

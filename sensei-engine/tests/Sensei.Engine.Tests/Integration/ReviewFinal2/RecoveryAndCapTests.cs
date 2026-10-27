using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sensei.Engine.Tests.Integration.ReviewFinal2;

/// <summary>F03: a response that needed blank-name recovery keeps every call id distinct and never runs a call beyond the budget.</summary>
public sealed class RecoveryDistinctIdTests
{
    private const string Cup = "{\"objects\":[{\"catalog_id\":\"cup\"}]}";

    private static async Task<(TurnResult Turn, TestEngine Engine, JsonElement NextRequest)> RunAsync(params (string Id, string Name, string Arguments)[] calls)
    {
        var server = new FakeChatServer().EnqueueToolCalls(calls).EnqueueText("done");
        var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "recovery-key-QX42", "fake-model", server.BaseUrl));
        var engine = await TestEngine.CreateAsync(chatClient: wire);
        var turn = await engine.Engine.RunTurnAsync("hello").WaitAsync(TimeSpan.FromSeconds(10));
        var next = JsonDocument.Parse(server.Requests[1].Body).RootElement.Clone();
        server.Dispose();
        return (turn, engine, next);
    }

    private static (string[] CallIds, string[] ResultIds) Ids(JsonElement request)
    {
        var messages = request.GetProperty("messages").EnumerateArray().ToList();
        var calls = messages.Where(m => m.TryGetProperty("tool_calls", out _)).SelectMany(m => m.GetProperty("tool_calls").EnumerateArray())
            .Select(c => c.GetProperty("id").GetString()!).ToArray();
        var results = messages.Where(m => m.GetProperty("role").GetString() == "tool").Select(m => m.GetProperty("tool_call_id").GetString()!).ToArray();
        return (calls, results);
    }

    [Fact]
    public async Task F03_a_blank_call_then_a_call_with_the_same_id_get_distinct_ids_and_the_second_does_not_run()
    {
        var (turn, engine, next) = await RunAsync(("same", "", "{}"), ("same", "spawn_objects", Cup));
        await using var _ = engine;

        Assert.Equal(new[] { "", "spawn_objects" }, turn.ToolCalls.Select(c => c.Tool));
        Assert.Equal(new[] { ToolErrorCodes.UnknownTool, ToolErrorCodes.InvalidArguments }, turn.ToolCalls.Select(c => c.ErrorCode));
        Assert.Equal("same", turn.ToolCalls[0].CallId);
        Assert.StartsWith("auto-", turn.ToolCalls[1].CallId, StringComparison.Ordinal);
        Assert.Equal(ToolDetails.ReservedCallId, turn.ToolCalls[1].Result!.Detail);
        Assert.Empty(engine.Engine.GetScene().Instances);
        Assert.Equal(0, engine.Engine.GetScene().Revision);

        var (callIds, resultIds) = Ids(next);
        Assert.Equal(2, callIds.Distinct().Count());
        Assert.Equal(callIds, resultIds);
    }

    [Fact]
    public async Task F03_a_call_then_a_blank_call_with_the_same_id_runs_the_first_and_refuses_the_second()
    {
        var (turn, engine, next) = await RunAsync(("same", "spawn_objects", Cup), ("same", "", "{}"));
        await using var _ = engine;

        Assert.Equal(new[] { "spawn_objects", "" }, turn.ToolCalls.Select(c => c.Tool));
        Assert.Equal(ToolStatuses.Success, turn.ToolCalls[0].Status);
        Assert.Equal(ToolErrorCodes.InvalidArguments, turn.ToolCalls[1].ErrorCode);
        Assert.Equal("cup_1", Assert.Single(engine.Engine.GetScene().Instances).Id);

        var (callIds, resultIds) = Ids(next);
        Assert.Equal(2, callIds.Distinct().Count());
        Assert.Equal(callIds, resultIds);
    }

    [Fact]
    public async Task F03_repeated_ids_never_let_a_mutation_run_beyond_the_two_validation_failures_of_the_turn()
    {
        var (turn, engine, next) = await RunAsync(("a", "", "{}"), ("a", "", "{}"), ("a", "spawn_objects", Cup));
        await using var _ = engine;

        Assert.Equal(new[] { ToolErrorCodes.UnknownTool, ToolErrorCodes.InvalidArguments, ToolErrorCodes.TurnBudgetExhausted }, turn.ToolCalls.Select(c => c.ErrorCode));
        Assert.Empty(engine.Engine.GetScene().Instances);

        var (callIds, resultIds) = Ids(next);
        Assert.Equal(3, callIds.Distinct().Count());
        Assert.Equal(callIds, resultIds);
    }

    [Fact]
    public async Task F03_a_third_call_with_the_same_id_after_a_blank_one_is_renamed_too()
    {
        var (turn, engine, next) = await RunAsync(("a", "", "{}"), ("b", "inspect_scene", "{}"), ("a", "inspect_scene", "{}"), ("a", "inspect_scene", "{}"));
        await using var _ = engine;

        var (callIds, resultIds) = Ids(next);
        Assert.Equal(4, callIds.Distinct().Count());
        Assert.Equal(new[] { "a", "b" }, callIds.Take(2));
        Assert.All(callIds.Skip(2), id => Assert.StartsWith("auto-", id, StringComparison.Ordinal));
        Assert.Equal(callIds, resultIds);
        Assert.Equal(ToolStatuses.Success, turn.ToolCalls[1].Status);
    }

    [Fact]
    public async Task F03_without_a_blank_call_the_ids_the_provider_gave_are_left_as_they_are()
    {
        var (turn, engine, next) = await RunAsync(("same", "inspect_scene", "{}"), ("same", "inspect_scene", "{}"));
        await using var _ = engine;

        Assert.Equal(new[] { false, true }, turn.ToolCalls.Select(c => c.Duplicate));
        Assert.Equal(new[] { "same", "same" }, Ids(next).CallIds);
    }

    [Fact]
    public async Task F03_unique_ids_with_a_blank_call_keep_the_provider_order_and_ids()
    {
        var (turn, engine, next) = await RunAsync(("x", "inspect_scene", "{}"), ("y", "", "{}"), ("z", "inspect_scene", "{}"));
        await using var _ = engine;

        Assert.Equal(new[] { "inspect_scene", "", "inspect_scene" }, turn.ToolCalls.Select(c => c.Tool));
        Assert.Equal(new[] { "x", "y", "z" }, Ids(next).CallIds);
    }

    [Fact]
    public async Task F03_engine_call_ids_are_unique_in_the_engine_instance()
    {
        await using var t = await TestEngine.CreateAsync();
        var pipeline = t.Core.Pipeline;

        var first = pipeline.ReserveEngineCallId();
        var second = pipeline.ReserveEngineCallId();
        t.Engine.BeginTurn("x");
        var assigned = (await t.CallAsync("inspect_scene", "{}")).CallId;

        Assert.StartsWith("auto-", first, StringComparison.Ordinal);
        Assert.Equal(3, new[] { first, second, assigned }.Distinct().Count());
    }
}

/// <summary>F01: a JSON element or node inside a loosely typed value counts against the same 10,000-part limit as everything else.</summary>
public sealed class SharedNodeCapTests
{
    private const string Key = "d6-cap-key-QX42-example";

    private static readonly Secrets Known = new(new[] { Key });

    private static JsonElement IntArray(int items) => JsonDocument.Parse("[" + string.Join(",", Enumerable.Repeat("1", items)) + "]").RootElement.Clone();

    private static ChatMessage ResultOf(object value) => new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("r", value) });

    [Theory]
    [InlineData("element", 9_999, false)]
    [InlineData("element", 10_000, true)]
    [InlineData("node", 9_999, false)]
    [InlineData("node", 10_000, true)]
    public void F01_a_json_value_is_kept_up_to_exactly_the_limit_including_its_own_root(string shape, int items, bool replaced)
    {
        var value = IntArray(items);
        object result = shape == "element" ? value : JsonNode.Parse(value.GetRawText())!;
        var notes = new List<string>();

        var sent = OutboundMessages.Sanitized(new[] { ResultOf(result) }, Known, notes);

        var actual = Assert.Single(sent).Contents.OfType<FunctionResultContent>().Single().Result;
        if (replaced)
        {
            Assert.Equal("[oversized value]", actual);
            Assert.Single(notes);
        }
        else
        {
            Assert.IsType<JsonElement>(actual);
            Assert.Empty(notes);
        }
    }

    [Fact]
    public void F01_a_clr_list_and_a_json_element_share_one_count()
    {
        // Each alone fits (5,001 parts); together with their parent they are 10,003.
        var value = new Dictionary<string, object?>
        {
            ["list"] = Enumerable.Repeat<object?>("x", 5_000).ToList(),
            ["json"] = IntArray(5_000),
        };

        var clean = JsonRedaction.SanitizedValue(value, Known, out var oversized);

        Assert.True(oversized);
        Assert.Equal("[oversized value]", clean);
    }

    [Fact]
    public void F01_the_same_two_parts_fit_when_one_part_is_smaller()
    {
        var value = new Dictionary<string, object?>
        {
            ["list"] = Enumerable.Repeat<object?>("x", 5_000).ToList(),
            ["json"] = IntArray(4_990),
        };

        JsonRedaction.SanitizedValue(value, Known, out var oversized);

        Assert.False(oversized);
    }

    [Fact]
    public void F01_the_strings_inside_a_json_element_are_still_cleaned()
    {
        var element = JsonDocument.Parse("{\"" + Key + "\":[\"a " + Key + "\"]}").RootElement.Clone();

        var clean = JsonRedaction.SanitizedValue(element, Known, out var oversized);

        Assert.False(oversized);
        Assert.Equal("{\"[redacted]\":[\"a [redacted]\"]}", ((JsonElement)clean!).GetRawText());
    }

    [Fact]
    public void F01_a_json_element_nested_deeper_than_any_stack_is_cleaned_without_recursion()
    {
        var depth = 5_000;
        var json = new string('[', depth) + "\"" + Key + "\"" + new string(']', depth);
        var element = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = depth + 1 }).RootElement.Clone();

        var clean = JsonRedaction.SanitizedValue(element, Known, out var oversized);

        Assert.False(oversized);
        Assert.DoesNotContain(Key, ((JsonElement)clean!).GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void F01_a_call_argument_that_is_a_big_json_element_is_replaced_by_an_empty_object()
    {
        var call = new FunctionCallContent("c", "inspect_scene", new Dictionary<string, object?> { ["extra"] = IntArray(10_001) });
        var notes = new List<string>();

        var sent = OutboundMessages.Sanitized(new[] { new ChatMessage(ChatRole.Assistant, new List<AIContent> { call }) }, Known, notes);

        Assert.Empty(Assert.Single(sent).Contents.OfType<FunctionCallContent>().Single().Arguments!);
        Assert.Single(notes);
    }
}

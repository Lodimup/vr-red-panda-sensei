using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Trace;
using static Sensei.Engine.Tests.Integration.ReviewFinal4.ReviewFinal4Tests;

namespace Sensei.Engine.Tests.Integration.ReviewFinal5;

/// <summary>
/// Repair tests for final review round 5, F02 (S133). A CLR-backed <see cref="JsonValue"/> is written by the serializer, an object it writes
/// may hold a node, and that node may hold a CLR-backed value again: every hand-over starts a new serializer and a new walk, so only a
/// counter carried by the walk bounds the chain. A cycle through a node ended the process; here the chains are finite and acyclic (the
/// cyclic case runs in a child process in <see cref="CycleRegressionTests"/>). Every value is invented; no network, no key.
/// </summary>
public sealed class ReentryTests
{
    public sealed record Plain(string Text, int Count);

    public sealed record NodeBox(JsonNode Node);

    /// <summary>An object whose getter counts how often it is read.</summary>
    public sealed class Counted
    {
        [JsonIgnore]
        public int[] Reads { get; init; } = new int[1];

        public int Value
        {
            get
            {
                Reads[0]++;
                return 1;
            }
        }
    }

    public sealed class Layer
    {
        public Layer? Next { get; set; }

        public JsonNode? Node { get; set; }
    }

    /// <summary>A chain of <paramref name="leaves"/> CLR-backed values, each holding the next one in a node; the last holds a record.</summary>
    private static JsonNode Chain(int leaves)
    {
        JsonNode node = JsonValue.Create(new Plain("hello", 1))!;
        for (var i = 1; i < leaves; i++) node = JsonValue.Create(new NodeBox(node))!;
        return node;
    }

    [Fact]
    public void The_re_entry_limit_is_eight()
    {
        Assert.Equal(8, JsonRedaction.ClrReentryLimit);
    }

    // ---- an acyclic chain up to the limit is written, past it is refused ---------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(8)]
    public void A_chain_of_up_to_eight_clr_backed_values_is_written_by_the_in_memory_sanitizer_and_the_host_guard(int leaves)
    {
        var chain = Chain(leaves);

        var clean = Assert.IsType<JsonElement>(JsonRedaction.SanitizedValue(chain, Secrets.None, out var oversized));
        Assert.False(oversized);
        Assert.Contains("\"hello\"", clean.GetRawText(), StringComparison.Ordinal);

        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
        var messages = new List<ChatMessage> { ResultMessage(chain) };
        Assert.Same(messages, guard.Guarded(messages));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(200)]
    public void A_chain_of_nine_or_more_is_not_serializable_in_the_in_memory_sanitizer_the_host_guard_and_call_arguments(int leaves)
    {
        var chain = Chain(leaves);

        var rejected = Assert.Throws<OutboundRejectedException>(() =>
            OutboundMessages.Sanitized(new[] { ResultMessage(chain) }, Secrets.None, new List<string>()));
        Assert.Equal("not_serializable", rejected.Reason);
        Assert.Null(rejected.InnerException);

        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
        Assert.Equal("not_serializable", Assert.Throws<OutboundRejectedException>(() => guard.Guarded(new List<ChatMessage> { ResultMessage(chain) })).Reason);

        var arguments = new FunctionCallContent("c", "inspect_scene", new Dictionary<string, object?> { ["x"] = chain });
        Assert.Equal("not_serializable", Assert.Throws<OutboundRejectedException>(() =>
            OutboundMessages.Sanitized(new[] { new ChatMessage(ChatRole.Assistant, new List<AIContent> { arguments }) }, Secrets.None, new List<string>())).Reason);
    }

    [Fact]
    public void A_chain_inside_a_record_that_is_serialized_directly_is_bounded_too()
    {
        // The count starts at the first leaf, wherever the walk was entered from: here the serializer reaches the node first.
        var rejected = Assert.Throws<OutboundRejectedException>(() =>
            OutboundMessages.Sanitized(new[] { ResultMessage(new NodeBox(Chain(9))) }, Secrets.None, new List<string>()));
        Assert.Equal("not_serializable", rejected.Reason);

        var clean = Assert.IsType<JsonElement>(JsonRedaction.SanitizedValue(new NodeBox(Chain(8)), Secrets.None, out var oversized));
        Assert.False(oversized);
        Assert.Contains("\"hello\"", clean.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_chain_leaves_the_counter_where_it_was()
    {
        // The count is per thread and restored on the way out, a failure included: the same thread writes a chain of eight right after.
        for (var round = 0; round < 3; round++)
        {
            Assert.Throws<OutboundRejectedException>(() =>
                OutboundMessages.Sanitized(new[] { ResultMessage(Chain(9)) }, Secrets.None, new List<string>()));
            Assert.IsType<JsonElement>(JsonRedaction.SanitizedValue(Chain(8), Secrets.None, out var oversized));
            Assert.False(oversized);
        }
    }

    [Fact]
    public async Task Two_threads_each_write_a_chain_of_eight_at_the_same_time()
    {
        // Nothing is shared between threads: a count that were global would refuse one of them.
        using var gate = new Barrier(2);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Factory.StartNew(() =>
        {
            gate.SignalAndWait();
            return JsonRedaction.SanitizedValue(Chain(8), Secrets.None, out var oversized) is JsonElement && !oversized;
        }, TaskCreationOptions.LongRunning)));
        Assert.All(results, Assert.True);
    }

    // ---- through the engine: a failed turn, one rejection trace, a live host --------------------------------------------------------------------

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public async Task A_chain_through_a_host_client_completes_up_to_eight_and_is_refused_past_it(int leaves, bool completes)
    {
        var chain = Chain(leaves);
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(ResultMessage(chain))))
            .EnqueueText("done");
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(chat: chat, trace: trace);

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        var turn = await t.Engine.RunTurnAsync("two");

        if (completes)
        {
            Assert.Equal(TurnStatuses.Completed, turn.Status);
            Assert.Equal(2, chat.RequestCount);
            Assert.DoesNotContain(trace.Entries, e => e.ErrorCode == "outbound_rejected");
            return;
        }
        Assert.Equal(TurnStatuses.Failed, turn.Status);
        Assert.Equal(TurnErrorCodes.ProviderError, turn.ErrorCode);
        Assert.Equal("outbound request rejected: not_serializable", turn.ErrorDetail);
        Assert.Equal(1, chat.RequestCount);
        var entry = Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
        Assert.Equal("engine:outbound_rejected", entry.Tool);
        Assert.Equal("request not sent: not_serializable", entry.Arguments);
    }

    [Fact]
    public async Task A_chain_of_nine_through_the_real_adapter_sends_nothing()
    {
        using var server = new FakeChatServer().EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "review5-chain-example", "fake-model", server.BaseUrl));
        using var chat = new SeededWireClient(wire, Chain(9));
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(chatClient: chat, trace: trace);

        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        var turn = await t.Engine.RunTurnAsync("two");

        Assert.Equal(TurnStatuses.Failed, turn.Status);
        Assert.Equal("outbound request rejected: not_serializable", turn.ErrorDetail);
        Assert.Empty(server.Requests);
        Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
    }

    // ---- the walks below the first share the count of the walk above them -------------------------------------------------------------------

    [Fact]
    public void A_walk_below_a_walk_stops_when_the_limit_above_it_is_used_up()
    {
        // The outer node is a list of 19 990 numbers and then a CLR-backed value that holds a node of 100 objects whose getter counts reads.
        // That node is re-entered: it continues the count of the list instead of starting a limit of its own, so it stops after a handful of
        // objects (the limit is 20 000) and the value is refused; with a limit of its own it would read all 100 before anything was judged.
        var reads = new int[1];
        var inner = new JsonArray();
        for (var i = 0; i < 100; i++) inner.Add(JsonValue.Create(new Counted { Reads = reads }));
        var outer = new JsonArray();
        for (var i = 0; i < 19_990; i++) outer.Add(i);
        outer.Add(JsonValue.Create(new NodeBox(inner)));
        var value = JsonValue.Create(new NodeBox(outer))!;

        Assert.ThrowsAny<Exception>(() => JsonRedaction.SanitizedValue(value, Secrets.None, out _));

        Assert.InRange(reads[0], 1, 10);
    }

    [Fact]
    public void A_re_entered_walk_that_fits_the_limit_above_it_is_written_whole()
    {
        var reads = new int[1];
        var inner = new JsonArray();
        for (var i = 0; i < 100; i++) inner.Add(JsonValue.Create(new Counted { Reads = reads }));
        var outer = new JsonArray();
        for (var i = 0; i < 100; i++) outer.Add(i);
        outer.Add(JsonValue.Create(new NodeBox(inner)));
        var value = JsonValue.Create(new NodeBox(outer))!;

        var clean = Assert.IsType<JsonElement>(JsonRedaction.SanitizedValue(value, Secrets.None, out var oversized));

        Assert.False(oversized);
        Assert.Equal(100, reads[0]);
        // The outer record and its list, 100 numbers, the inner record and its list, 100 objects of one property each.
        Assert.Equal(1 + 1 + 100 + 1 + 1 + 100 * 2, ValueCount(clean.GetRawText()));
    }

    // ---- the stack: eight serializers in a row, each as deep as the serializer allows, on the smallest stack a host gives -----------------

    [Fact]
    public void Eight_re_entered_serializers_that_each_nest_sixty_levels_fit_a_one_mebibyte_stack()
    {
        // Each leaf in the chain is a CLR object 60 levels deep (the serializer's own limit is 64) that ends in the next leaf's node.
        JsonNode node = JsonValue.Create(new Plain("hello", 1))!;
        for (var leaf = 1; leaf < 8; leaf++)
        {
            var layer = new Layer { Node = node };
            for (var level = 1; level < 60; level++) layer = new Layer { Next = layer };
            node = JsonValue.Create(layer)!;
        }
        var failure = default(Exception);
        var thread = new Thread(() =>
        {
            try
            {
                var clean = Assert.IsType<JsonElement>(JsonRedaction.SanitizedValue(node, Secrets.None, out var oversized));
                Assert.False(oversized);
                Assert.Contains("\"hello\"", clean.GetRawText(), StringComparison.Ordinal);
            }
            catch (Exception ex) { failure = ex; }
        }, 1024 * 1024);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }
}

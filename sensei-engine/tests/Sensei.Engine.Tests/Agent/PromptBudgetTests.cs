using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.TestKit;
using Xunit.Abstractions;

namespace Sensei.Engine.Tests.Agent;

/// <summary>
/// Input-size guard for the agent request (s5). Every request carries the system prompt, the 16 tool schemas, the
/// current state and the conversation so far, and each input token costs time and money on every model round. This
/// measures what really goes on the wire through the real OpenAI client against the loopback server, with no provider.
/// </summary>
public sealed class PromptBudgetTests
{
    private const string Key = "budget-key-0123456789";

    /// <summary>
    /// Provider-reported input tokens per UTF-8 byte of the wire body: the first request of a fresh engine was 38574 bytes
    /// and 8054 provider tokens at commit 209a5a5 (live, 2026-10-28). Mixed JSON and prose, so an estimate, not a tokenizer.
    /// </summary>
    public const double TokensPerByte = 8054.0 / 38574.0;

    /// <summary>The input budget per request, in estimated tokens (the orchestrator's s5 target).</summary>
    public const int BudgetTokens = 8000;

    private readonly ITestOutputHelper _output;

    public PromptBudgetTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private static double Tokens(int bytes) => bytes * TokensPerByte;

    /// <summary>The first request of a turn on a fresh engine: total bytes and the share of the system prompt, the tools and the learner block.</summary>
    private static async Task<(int Total, int System, int Tools, int User, string State)> FirstRequestAsync(string transcript)
    {
        using var server = new FakeChatServer().EnqueueText("ok");
        var client = ChatClientFactory.Create(new LlmSettings("openrouter", Key, null, server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { Key });
        await t.Engine.RunTurnAsync(transcript);
        var request = server.Requests[0];
        var body = request.Json;
        var messages = body.GetProperty("messages").EnumerateArray().ToList();
        var system = string.Concat(messages.Where(m => m.GetProperty("role").GetString() == "system").Select(m => m.GetProperty("content").GetString()));
        var user = string.Concat(messages.Where(m => m.GetProperty("role").GetString() == "user").Select(m => m.GetProperty("content").GetString()));
        return (Bytes(request.Body), Bytes(system), Bytes(body.GetProperty("tools").GetRawText()), Bytes(user), system[system.IndexOf("## Current state", StringComparison.Ordinal)..]);
    }

    [Fact]
    public async Task The_first_request_of_a_turn_fits_the_input_budget_with_room_for_the_rounds()
    {
        var b = await FirstRequestAsync("Hello! I'm a complete beginner. Can you show me something to drink?");
        _output.WriteLine($"first request: {b.Total} bytes (~{Tokens(b.Total):F0} tokens): system+state {b.System}, tools {b.Tools}, learner block {b.User}");
        _output.WriteLine("state:\n" + b.State);
        // Room for the history of a few turns and the results of up to 3 more rounds inside the budget.
        Assert.True(Tokens(b.Total) <= BudgetTokens * 0.8, $"first request ~{Tokens(b.Total):F0} tokens, over {BudgetTokens * 0.8:F0}");
    }

    [Fact]
    public async Task The_tools_go_out_with_their_schema_written_compactly()
    {
        using var server = new FakeChatServer().EnqueueText("ok");
        var client = ChatClientFactory.Create(new LlmSettings("openrouter", Key, null, server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { Key });
        await t.Engine.RunTurnAsync("hi");

        var body = server.Requests[0].Json;
        var tools = body.GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(t.Engine.ToolNames.ToArray(), tools.Select(x => x.GetProperty("function").GetProperty("name").GetString()!).ToArray());
        Assert.Equal("auto", body.GetProperty("tool_choice").GetString());
        Assert.True(body.GetProperty("parallel_tool_calls").GetBoolean()); // the loop is built for several calls in one response
        foreach (var tool in tools)
        {
            var schema = tool.GetProperty("function").GetProperty("parameters").GetRawText();
            Assert.DoesNotContain("\n", schema);
            Assert.DoesNotContain("  ", schema);
        }
    }

    [Fact]
    public async Task A_compact_schema_means_the_same_schema()
    {
        using var server = new FakeChatServer().EnqueueText("ok");
        var client = ChatClientFactory.Create(new LlmSettings("openrouter", Key, null, server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { Key });
        await t.Engine.RunTurnAsync("hi");

        var onWire = server.Requests[0].Json.GetProperty("tools").EnumerateArray()
            .ToDictionary(x => x.GetProperty("function").GetProperty("name").GetString()!, x => x.GetProperty("function"));
        foreach (var tool in t.Core.Tools.Standard)
        {
            var sent = onWire[tool.Name];
            Assert.Equal(tool.Description, sent.GetProperty("description").GetString());
            Assert.True(JsonEquals(tool.JsonSchema, sent.GetProperty("parameters")), $"{tool.Name}: the schema on the wire differs from the engine's schema");
        }
    }

    private static bool JsonEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var left = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var right = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                return left.Count == right.Count && left.All(kv => right.TryGetValue(kv.Key, out var other) && JsonEquals(kv.Value, other));
            case JsonValueKind.Array:
                var l = a.EnumerateArray().ToList();
                var r = b.EnumerateArray().ToList();
                return l.Count == r.Count && l.Zip(r, JsonEquals).All(x => x);
            case JsonValueKind.String:
                return a.GetString() == b.GetString(); // the writer may escape an apostrophe the checked-in text does not
            default:
                return a.GetRawText() == b.GetRawText();
        }
    }

    // ---------------------------------------------------------------- a four-turn session, replayed from a real run

    private static readonly (string Learner, (string Name, string Args)[][] Rounds)[] Session =
    {
        ("Hello! I'm a complete beginner. Can you show me something to drink?", new[]
        {
            new[]
            {
                ("speak", "{\"segments\":[{\"lang\":\"ja\",\"text\":\"こんにちは！みずです。\",\"romaji\":\"konnichiwa! mizu desu.\",\"translation\":\"Hello! This is water.\"},{\"lang\":\"en\",\"text\":\"You are a beginner, so let's start with something to drink. Here is water and tea!\"}]}"),
                ("spawn_objects", "{\"objects\":[{\"catalog_id\":\"water\",\"quantity\":1,\"region\":\"center\"},{\"catalog_id\":\"tea\",\"quantity\":1,\"region\":\"left\"},{\"catalog_id\":\"cup\",\"quantity\":1,\"region\":\"right\"}]}"),
            },
            new[] { ("highlight_objects", "{\"duration_ms\":2000,\"object_ids\":[\"water_1\",\"tea_1\"]}") },
            new[] { ("start_challenge", "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"Ask for water in Japanese. Say 'mizu o kudasai'.\",\"japanese\":\"みずをくださいと言ってみましょう。\"},\"is_fresh_variation\":false,\"predicates\":[{\"type\":\"selected\",\"object_id\":\"water_1\"}],\"skill_targets\":[{\"dimension\":\"vocabulary\",\"japanese\":\"みず\",\"key\":\"water\"}]}") },
            new[] { ("speak", "{\"segments\":[{\"lang\":\"ja\",\"text\":\"みずをください。\",\"romaji\":\"mizu o kudasai.\",\"translation\":\"Water, please.\"},{\"lang\":\"en\",\"text\":\"Say this to ask for water. I will give it to you!\"}]}") },
        }),
        ("Mizu o kudasai.", new[]
        {
            new[] { ("speak", "{\"segments\":[{\"lang\":\"ja\",\"reading\":\"はい、みずです。\",\"romaji\":\"hai, mizu desu.\",\"text\":\"はい、みずです。\",\"translation\":\"Yes, here is water.\"},{\"lang\":\"en\",\"text\":\"Great asking! Here you go!\"}]}") },
            new[]
            {
                ("move_object", "{\"destination\":{\"region\":\"learner_hand\"},\"object_id\":\"water_1\",\"purpose\":\"fulfill_request\"}"),
                ("record_attempt", "{\"communicative_success\":true,\"language_assessment\":\"correct\",\"target\":{\"dimension\":\"vocabulary\",\"japanese\":\"みず\",\"key\":\"water\"},\"task_id\":\"t1\",\"transcript_excerpt\":\"Mizu o kudasai.\"}"),
            },
            new[] { ("highlight_objects", "{\"duration_ms\":2000,\"object_ids\":[\"water_1\"]}") },
        }),
        ("Thank you! What should we do next?", new[]
        {
            new[] { ("speak", "{\"segments\":[{\"lang\":\"ja\",\"reading\":\"どういたしまして！\",\"romaji\":\"dou itashimashite!\",\"text\":\"どういたしまして！\",\"translation\":\"You're welcome!\"},{\"lang\":\"en\",\"text\":\"Great job asking for water! Want to try tea next, or practice something else?\"}]}") },
            new[] { ("update_challenge", "{\"action\":\"abandon\",\"reason\":\"Learner asked what to do next, moving on from water request.\",\"task_id\":\"t1\"}") },
        }),
        ("おちゃをください。", new[]
        {
            new[] { ("speak", "{\"segments\":[{\"lang\":\"ja\",\"text\":\"はい、おちゃです。どうぞ！\",\"romaji\":\"hai, ocha desu. douzo!\",\"translation\":\"Yes, here is tea! Here you go!\"},{\"lang\":\"en\",\"text\":\"Perfect asking! Here is your tea!\"}]}") },
            new[] { ("move_object", "{\"destination\":{\"region\":\"learner_hand\"},\"object_id\":\"tea_1\",\"purpose\":\"fulfill_request\"}") },
            new[] { ("record_attempt", "{\"communicative_success\":true,\"language_assessment\":\"correct\",\"target\":{\"dimension\":\"vocabulary\",\"japanese\":\"おちゃ\",\"key\":\"tea\"},\"transcript_excerpt\":\"おちゃをください。\"}") },
        }),
    };

    /// <summary>Plays the session through the engine and returns the body size of every request, grouped by turn.</summary>
    internal static async Task<List<List<int>>> PlaySessionAsync()
    {
        using var server = new FakeChatServer();
        var n = 0;
        foreach (var (_, rounds) in Session)
        {
            foreach (var round in rounds) server.EnqueueToolCalls(round.Select(c => ($"call_{++n}", c.Name, c.Args)).ToArray());
            if (rounds.Length < 4) server.EnqueueText(""); // a fourth round ends the turn without another request
        }
        var client = ChatClientFactory.Create(new LlmSettings("openrouter", Key, null, server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { Key });
        var perTurn = new List<List<int>>();
        var seen = 0;
        foreach (var (learner, rounds) in Session)
        {
            var result = await t.Engine.RunTurnAsync(learner);
            Assert.True(result.Status is TurnStatuses.Completed or TurnStatuses.BudgetExhausted, $"{learner}: {result.Status} {result.ErrorDetail}");
            var sizes = server.Requests.Skip(seen).Select(r => Bytes(r.Body)).ToList();
            seen = server.Requests.Count;
            perTurn.Add(sizes);
        }
        return perTurn;
    }

    [Fact]
    public async Task Every_request_of_a_four_turn_session_fits_the_input_budget()
    {
        var turns = await PlaySessionAsync();
        for (var i = 0; i < turns.Count; i++)
            _output.WriteLine($"turn {i + 1}: " + string.Join(", ", turns[i].Select(b => $"{b} B (~{Tokens(b):F0} tok)")));
        var largest = turns.SelectMany(x => x).Max();
        _output.WriteLine($"largest request {largest} bytes (~{Tokens(largest):F0} tokens)");
        Assert.True(Tokens(largest) <= BudgetTokens, $"largest request ~{Tokens(largest):F0} tokens, over {BudgetTokens}");
    }
}

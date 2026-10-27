using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.Cli;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Integration.ReviewFinal5;

public sealed class ReviewFinal5Controls
{
    [Fact]
    public async Task C01_acyclic_clr_backed_json_value_completes_on_the_same_process_path()
    {
        var result = await CycleProcess.RunAsync(acyclic: true);
        Assert.True(result.Code == 0, result.Output);
        Assert.Contains("status=completed", result.Output);
    }

    [Fact]
    public async Task C02_tool_calls_results_and_transcripts_remain_available()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c", "inspect_scene", "{}")).EnqueueText("").EnqueueText("");
        await using var t = await TestEngine.CreateAsync(chat: chat);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);
        var history = chat.Requests.Last().Messages;
        Assert.Contains(history, m => m.Text.Contains("learner_transcript: one", StringComparison.Ordinal));
        Assert.Contains(history.SelectMany(m => m.Contents).OfType<FunctionCallContent>(), c => c.CallId == "c");
        Assert.Contains(history.SelectMany(m => m.Contents).OfType<FunctionResultContent>(), c => c.CallId == "c");
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void C03_argument_depth_matches_the_parser(int depth, bool replaced)
    {
        var raw = new string('[', depth - 1) + "0" + new string(']', depth - 1);
        using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 100 });
        var call = new FunctionCallContent("c", "inspect_scene", new Dictionary<string, object?> { ["x"] = document.RootElement.Clone() });
        var notes = new List<string>();
        var clean = OutboundMessages.Sanitized(new[] { new ChatMessage(ChatRole.Assistant, new List<AIContent> { call }) }, Secrets.None, notes);
        var arguments = clean.Single().Contents.OfType<FunctionCallContent>().Single().Arguments!;
        Assert.Equal(replaced, arguments.Count == 0);
        Assert.Equal(replaced ? 1 : 0, notes.Count);
    }

    [Fact]
    public async Task C04_wire_deep_result_and_known_secret_are_handled_inside_the_boundary()
    {
        using var document = JsonDocument.Parse(new string('[', 200) + "\"fictional-known-value\"" + new string(']', 200), new JsonDocumentOptions { MaxDepth = 20_001 });
        using var server = new FakeChatServer().EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "fictional-wire-key", "fake-model", server.BaseUrl));
        using var chat = new Integration.ReviewFinal4.ReviewFinal4Tests.SeededWireClient(wire, document.RootElement.Clone());
        await using var t = await TestEngine.CreateAsync(chatClient: chat, knownSecrets: new[] { "fictional-known-value" });
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);
        var request = Assert.Single(server.Requests);
        Assert.DoesNotContain("fictional-known-value", request.Body);
        Assert.Contains("[redacted]", request.Body);
        Assert.Equal("Bearer fictional-wire-key", request.Authorization);
    }

    [Fact]
    public async Task C05_cli_manual_gameplay_persists_and_restarts_with_a_clean_scene()
    {
        var first = await InProcessCli.ScriptAsync(new[]
        {
            "/band intermediate", "/pref show_romaji true",
            "/tool spawn_objects {\"objects\":[{\"catalog_id\":\"apple\",\"region\":\"left\"}]}",
            "/tool remember_note {\"key\":\"favorite\",\"value\":\"apple\"}", "/scene", "/quit",
        });
        Assert.Equal(0, first.Code);
        Assert.Contains("apple_1 | apple | left", first.Text);
        var second = await InProcessCli.ScriptAsync(new[] { "/profile", "/scene", "/quit" }, profileDir: first.ProfileDir);
        Assert.Equal(0, second.Code);
        Assert.Contains("starting band: intermediate", second.Text);
        Assert.Contains("pinned: show_romaji", second.Text);
        Assert.Contains("favorite=apple", second.Text);
        Assert.Contains("(the room is empty)", second.Text);
    }
}

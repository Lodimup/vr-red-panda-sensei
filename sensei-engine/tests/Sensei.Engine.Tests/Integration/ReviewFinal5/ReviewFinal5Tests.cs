using Microsoft.Extensions.AI;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Integration.ReviewFinal5;

public sealed class ReviewFinal5Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F01_plain_assistant_text_is_trace_only_and_must_not_be_persisted(bool withToolCall)
    {
        const string unspoken = "I moved the apple earlier; unspoken historical statement.";
        var contents = new List<AIContent> { new TextContent(unspoken) };
        if (withToolCall) contents.Add(new FunctionCallContent("c", "inspect_scene", new Dictionary<string, object?>()));
        var chat = new ScriptedChatClient().Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))));
        if (withToolCall) chat.EnqueueText("");
        chat.EnqueueText("done");
        await using var t = await TestEngine.CreateAsync(chat: chat);
        var first = await t.Engine.RunTurnAsync("one");
        Assert.Equal(TurnStatuses.Completed, first.Status);
        Assert.Equal(unspoken, first.UnspokenText);
        Assert.Empty(t.Speech.Requests);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);
        Assert.DoesNotContain(chat.Requests.Last().Messages.SelectMany(m => m.Contents).OfType<TextContent>(),
            c => c.Text?.Contains(unspoken, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task F01_unspoken_assistant_text_must_not_reach_the_next_wire_request()
    {
        const string unspoken = "Unspoken historical statement from the previous turn.";
        using var server = new FakeChatServer().EnqueueText(unspoken).EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", "review-five-invented", "fake-model", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: wire);
        Assert.Equal(unspoken, (await t.Engine.RunTurnAsync("one")).UnspokenText);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);
        Assert.Equal(2, server.Requests.Count);
        Assert.DoesNotContain(unspoken, server.Requests[1].Body, StringComparison.Ordinal);
    }
}

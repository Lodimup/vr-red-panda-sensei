using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Integration;

/// <summary>S77 chokepoint: the strings of a turn result are cleaned before the host sees them, and so is every message sent back to the provider.</summary>
public sealed class OutboundSanitizingTests
{
    private const string Secret = "ifix-known-secret-QX42";

    [Fact]
    public async Task S77_turn_result_spoken_text_has_known_secrets_redacted()
    {
        var args = "{\"segments\":[{\"lang\":\"en\",\"text\":\"the word is " + Secret + "\"}]}";
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c1", "speak", args)).EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Secret });

        var result = await t.Engine.RunTurnAsync("hello");

        var spoken = Assert.Single(result.Spoken);
        Assert.DoesNotContain(Secret, spoken.Segments[0].Text);
        Assert.Contains("[redacted]", spoken.Segments[0].Text);
    }

    [Fact]
    public async Task S77_function_results_in_history_are_sanitized_before_they_go_back_to_the_provider()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("secret")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Secret });

        await t.Engine.RunTurnAsync("hello");

        foreach (var request in chat.Requests) Assert.DoesNotContain(Secret, Everything(request));
    }

    [Fact]
    public async Task S77_a_secret_the_provider_put_in_a_tool_call_name_is_not_sent_back()
    {
        var chat = new ScriptedChatClient().EnqueueFunctionCalls(("c" + Secret, "tool_" + Secret, "{}")).EnqueueText("done");
        await using var t = await EngineAsync(chat, knownSecrets: new[] { Secret });

        var result = await t.Engine.RunTurnAsync("hello");

        Assert.Equal(TurnStatuses.Completed, result.Status);
        foreach (var request in chat.Requests)
        {
            Assert.DoesNotContain(Secret, Everything(request));
            Assert.DoesNotContain(Secret, string.Join("|", CallsIn(request).Select(c => c.CallId + c.Name)));
            Assert.DoesNotContain(Secret, string.Join("|", ResultsIn(request).Select(r => r.CallId)));
        }
    }
}

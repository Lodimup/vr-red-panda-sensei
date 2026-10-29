using System.Text.Json;
using Microsoft.Extensions.AI;
using Sensei.Engine.Llm;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Agent;

/// <summary>S125: reasoning effort and the output-token bound, from the environment to the bytes on the wire.</summary>
public sealed class ReasoningEffortTests
{
    private const string Key = "effort-key-0123456789";

    private static Dictionary<string, string?> Env(params (string Name, string? Value)[] values)
    {
        var env = new Dictionary<string, string?> { ["SENSEI_LLM_API_KEY"] = Key };
        foreach (var (name, value) in values) env[name] = value;
        return env;
    }

    // ---------------------------------------------------------------- settings

    [Fact]
    public void S125_defaults_are_low_effort_and_2000_output_tokens()
    {
        var s = LlmSettings.FromEnvironment(Env());
        Assert.Equal("minimal", s.ReasoningEffort);
        Assert.Equal(2000, s.MaxOutputTokens);
        Assert.Equal("minimal", new LlmSettings("openrouter", Key).ReasoningEffort);
        Assert.Equal(2000, new LlmSettings("openrouter", Key).MaxOutputTokens);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("minimal")]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public void S125_every_allowed_effort_is_accepted(string effort)
    {
        Assert.Equal(effort, LlmSettings.FromEnvironment(Env(("SENSEI_LLM_REASONING_EFFORT", effort))).ReasoningEffort);
    }

    [Fact]
    public void S125_values_are_trimmed_and_empty_counts_as_missing()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_REASONING_EFFORT", "  high "), ("SENSEI_LLM_MAX_OUTPUT_TOKENS", " 640 ")));
        Assert.Equal("high", s.ReasoningEffort);
        Assert.Equal(640, s.MaxOutputTokens);

        var empty = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_REASONING_EFFORT", "  "), ("SENSEI_LLM_MAX_OUTPUT_TOKENS", "")));
        Assert.Equal("minimal", empty.ReasoningEffort);
        Assert.Equal(2000, empty.MaxOutputTokens);
    }

    [Theory]
    [InlineData("extreme")]
    [InlineData("LOW")]
    public void S125_an_unknown_effort_is_a_configuration_error_naming_the_allowed_values(string effort)
    {
        var error = Assert.Throws<LlmConfigurationException>(() => LlmSettings.FromEnvironment(Env(("SENSEI_LLM_REASONING_EFFORT", effort))));
        Assert.Contains("SENSEI_LLM_REASONING_EFFORT", error.Message);
        foreach (var allowed in new[] { "none", "minimal", "low", "medium", "high" }) Assert.Contains(allowed, error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void S125_an_unusable_token_bound_is_a_configuration_error(string tokens)
    {
        var error = Assert.Throws<LlmConfigurationException>(() => LlmSettings.FromEnvironment(Env(("SENSEI_LLM_MAX_OUTPUT_TOKENS", tokens))));
        Assert.Contains("SENSEI_LLM_MAX_OUTPUT_TOKENS", error.Message);
    }

    [Fact]
    public void S125_the_constructor_applies_the_same_rules()
    {
        var s = new LlmSettings("openrouter", Key, null, null, "medium", 512);
        Assert.Equal("medium", s.ReasoningEffort);
        Assert.Equal(512, s.MaxOutputTokens);
        Assert.Throws<LlmConfigurationException>(() => new LlmSettings("openrouter", Key, null, null, "extreme"));
        Assert.Throws<LlmConfigurationException>(() => new LlmSettings("openrouter", Key, null, null, null, 0));
    }

    [Fact]
    public void S125_the_provider_base_url_and_key_checks_come_first()
    {
        var provider = Assert.Throws<LlmConfigurationException>(() =>
            LlmSettings.FromEnvironment(Env(("SENSEI_LLM_PROVIDER", "mystery"), ("SENSEI_LLM_REASONING_EFFORT", "extreme"), ("SENSEI_LLM_MAX_OUTPUT_TOKENS", "0"))));
        Assert.Contains("SENSEI_LLM_PROVIDER", provider.Message);

        var key = Assert.Throws<LlmConfigurationException>(() =>
            LlmSettings.FromEnvironment(new Dictionary<string, string?> { ["SENSEI_LLM_REASONING_EFFORT"] = "extreme", ["SENSEI_LLM_MAX_OUTPUT_TOKENS"] = "0" }));
        Assert.Equal("SENSEI_LLM_API_KEY is not set", key.Message);

        var effort = Assert.Throws<LlmConfigurationException>(() =>
            LlmSettings.FromEnvironment(Env(("SENSEI_LLM_REASONING_EFFORT", "extreme"), ("SENSEI_LLM_MAX_OUTPUT_TOKENS", "0"))));
        Assert.Contains("SENSEI_LLM_REASONING_EFFORT", effort.Message);
    }

    [Fact]
    public void S125_to_string_shows_the_effort_and_the_bound_and_still_hides_the_key()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_REASONING_EFFORT", "high"), ("SENSEI_LLM_MAX_OUTPUT_TOKENS", "300")));
        Assert.Equal("provider=openrouter model=meta/muse-spark-1.3-contributor base_url=https://openrouter.ai/api/v1 reasoning_effort=high max_output_tokens=300 api_key=[set]", s.ToString());
        Assert.DoesNotContain(Key, s.ToString());
    }

    // ---------------------------------------------------------------- the wire

    private static async Task<JsonElement> OneRequestBody(LlmSettings settings, FakeChatServer server, ChatOptions? options = null)
    {
        using IChatClient client = ChatClientFactory.Create(settings);
        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }, options);
        return Assert.Single(server.Requests).Json;
    }

    [Fact]
    public async Task S125_openrouter_gets_reasoning_effort_low_and_the_default_bound_on_the_wire()
    {
        // OpenRouter's own base URL cannot be used against a loopback server, so the preset logic is exercised through a
        // settings object that names the provider and points at the loopback endpoint.
        using var server = new FakeChatServer().EnqueueText("ok");
        var body = await OneRequestBody(new LlmSettings("openrouter", Key, null, server.BaseUrl), server);

        Assert.Equal("minimal", body.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(body.TryGetProperty("reasoning_effort", out _));
        Assert.Equal(2000, body.GetProperty("max_completion_tokens").GetInt32());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public async Task S125_the_configured_effort_and_bound_are_sent(string effort)
    {
        using var server = new FakeChatServer().EnqueueText("ok");
        var body = await OneRequestBody(new LlmSettings("openrouter", Key, null, server.BaseUrl, effort, 321), server);

        Assert.Equal(effort, body.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(321, body.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task S125_other_providers_get_the_openai_reasoning_effort_field()
    {
        using var server = new FakeChatServer().EnqueueText("ok");
        var body = await OneRequestBody(new LlmSettings("openai_compatible", Key, null, server.BaseUrl, "medium"), server);

        Assert.Equal("medium", body.GetProperty("reasoning_effort").GetString());
        Assert.False(body.TryGetProperty("reasoning", out _));
        Assert.Equal(2000, body.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task S125_a_caller_bound_wins_over_the_default()
    {
        using var server = new FakeChatServer().EnqueueText("ok");
        var body = await OneRequestBody(new LlmSettings("openrouter", Key, null, server.BaseUrl), server, new ChatOptions { MaxOutputTokens = 77 });

        Assert.Equal(77, body.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("minimal", body.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task S125_the_callers_options_object_is_not_modified()
    {
        using var server = new FakeChatServer().EnqueueText("ok");
        var options = new ChatOptions();
        await OneRequestBody(new LlmSettings("openrouter", Key, null, server.BaseUrl), server, options);

        Assert.Null(options.MaxOutputTokens);
        Assert.Null(options.RawRepresentationFactory);
    }

    [Fact]
    public async Task S125_every_model_request_of_an_agent_turn_carries_the_effort_and_the_bound()
    {
        using var server = new FakeChatServer()
            .EnqueueToolCalls(("call_1", "inspect_scene", "{}"))
            .EnqueueText("done");
        var client = ChatClientFactory.Create(new LlmSettings("openrouter", Key, null, server.BaseUrl, "medium", 900));
        await using var t = await TestEngine.CreateAsync(chatClient: client, knownSecrets: new[] { Key });

        var result = await t.Engine.RunTurnAsync("hello");

        Assert.Equal(2, result.Rounds);
        Assert.Equal(2, server.Requests.Count);
        foreach (var request in server.Requests)
        {
            Assert.Equal("medium", request.Json.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Equal(900, request.Json.GetProperty("max_completion_tokens").GetInt32());
        }
    }

    [Fact]
    public async Task S125_the_key_is_not_in_the_request_body()
    {
        using var server = new FakeChatServer().EnqueueText("ok");
        await OneRequestBody(new LlmSettings("openrouter", Key, null, server.BaseUrl), server);
        Assert.DoesNotContain(Key, server.Requests[0].Body);
    }
}

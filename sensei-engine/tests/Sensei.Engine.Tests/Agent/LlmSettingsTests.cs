using Microsoft.Extensions.AI;
using Sensei.Engine.Llm;

namespace Sensei.Engine.Tests.Agent;

public sealed class LlmSettingsTests
{
    private const string Key = "test-key-0123456789";

    private static Dictionary<string, string?> Env(params (string Name, string? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value);

    // ---------------------------------------------------------------- S4 environment table

    [Fact]
    public void S4_defaults_are_the_openrouter_preset_and_the_default_model()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_API_KEY", Key)));
        Assert.Equal("openrouter", s.Provider);
        Assert.Equal("https://openrouter.ai/api/v1", s.BaseUrl);
        Assert.Equal("meta/muse-spark-1.3-contributor", s.Model);
    }

    [Fact]
    public void S4_meta_provider_uses_the_meta_preset()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_PROVIDER", "meta"), ("SENSEI_LLM_API_KEY", Key)));
        Assert.Equal("meta", s.Provider);
        Assert.Equal("https://api.meta.ai/v1", s.BaseUrl);
    }

    [Fact]
    public void S4_openai_compatible_takes_its_base_url_from_the_environment()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_PROVIDER", "openai_compatible"), ("SENSEI_LLM_API_KEY", Key), ("SENSEI_LLM_BASE_URL", "https://llm.example.com/v1")));
        Assert.Equal("openai_compatible", s.Provider);
        Assert.Equal("https://llm.example.com/v1", s.BaseUrl);
    }

    [Fact]
    public void S4_openai_compatible_without_a_base_url_is_a_configuration_error()
    {
        var error = Assert.Throws<LlmConfigurationException>(() =>
            LlmSettings.FromEnvironment(Env(("SENSEI_LLM_PROVIDER", "openai_compatible"), ("SENSEI_LLM_API_KEY", Key))));
        Assert.Contains("SENSEI_LLM_BASE_URL", error.Message);
    }

    [Fact]
    public void S4_base_url_and_model_overrides_apply_to_a_preset_provider()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_API_KEY", Key), ("SENSEI_LLM_MODEL", "vendor/model-1"), ("SENSEI_LLM_BASE_URL", "https://proxy.example.com/api")));
        Assert.Equal("vendor/model-1", s.Model);
        Assert.Equal("https://proxy.example.com/api", s.BaseUrl);
        Assert.Equal("openrouter", s.Provider);
    }

    [Fact]
    public void S4_unknown_provider_names_the_allowed_values()
    {
        var error = Assert.Throws<LlmConfigurationException>(() =>
            LlmSettings.FromEnvironment(Env(("SENSEI_LLM_PROVIDER", "mystery"), ("SENSEI_LLM_API_KEY", Key))));
        Assert.Contains("openrouter", error.Message);
        Assert.Contains("meta", error.Message);
        Assert.Contains("openai_compatible", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void S4_missing_or_empty_key_is_a_configuration_error_with_the_exact_message(string? key)
    {
        var error = Assert.Throws<LlmConfigurationException>(() => LlmSettings.FromEnvironment(Env(("SENSEI_LLM_API_KEY", key))));
        Assert.Equal("SENSEI_LLM_API_KEY is not set", error.Message);
    }

    [Fact]
    public void S4_a_key_absent_from_the_dictionary_is_missing_too()
    {
        var error = Assert.Throws<LlmConfigurationException>(() => LlmSettings.FromEnvironment(Env()));
        Assert.Equal("SENSEI_LLM_API_KEY is not set", error.Message);
    }

    [Fact]
    public void S4_values_are_trimmed_and_empty_optional_values_count_as_missing()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_PROVIDER", "  meta  "), ("SENSEI_LLM_API_KEY", "  " + Key + "  "), ("SENSEI_LLM_MODEL", "   "), ("SENSEI_LLM_BASE_URL", "")));
        Assert.Equal("meta", s.Provider);
        Assert.Equal("meta/muse-spark-1.3-contributor", s.Model);
        Assert.Equal("https://api.meta.ai/v1", s.BaseUrl);
        Assert.DoesNotContain(Key + " ", s.ToString());
    }

    [Fact]
    public void S4_an_empty_provider_falls_back_to_openrouter()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_PROVIDER", ""), ("SENSEI_LLM_API_KEY", Key)));
        Assert.Equal("openrouter", s.Provider);
    }

    [Fact]
    public void S4_provider_names_are_matched_exactly()
    {
        Assert.Throws<LlmConfigurationException>(() => LlmSettings.FromEnvironment(Env(("SENSEI_LLM_PROVIDER", "OpenRouter"), ("SENSEI_LLM_API_KEY", Key))));
    }

    // ---------------------------------------------------------------- S5 ToString

    [Fact]
    public void S5_to_string_shows_provider_model_base_url_and_that_a_key_is_set()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_API_KEY", Key)));
        Assert.Equal("provider=openrouter model=meta/muse-spark-1.3-contributor base_url=https://openrouter.ai/api/v1 reasoning_effort=minimal max_output_tokens=2000 api_key=[set]", s.ToString());
    }

    [Fact]
    public void S5_to_string_never_contains_the_key()
    {
        var s = LlmSettings.FromEnvironment(Env(("SENSEI_LLM_API_KEY", Key)));
        Assert.DoesNotContain(Key, s.ToString());
        Assert.DoesNotContain(Key, $"{s}");
    }

    [Fact]
    public void S79_to_string_redacts_the_key_from_every_field()
    {
        var s = new LlmSettings("openai_compatible", Key, "model-" + Key, "https://llm.example.com/" + Key);
        var text = s.ToString();
        Assert.DoesNotContain(Key, text);
        Assert.Contains("[redacted]", text);
        Assert.EndsWith("api_key=[set]", text);
    }

    // ---------------------------------------------------------------- S6 public constructor

    [Fact]
    public void S6_the_constructor_applies_the_same_defaults()
    {
        var s = new LlmSettings("openrouter", Key);
        Assert.Equal("https://openrouter.ai/api/v1", s.BaseUrl);
        Assert.Equal("meta/muse-spark-1.3-contributor", s.Model);
    }

    [Fact]
    public void S6_the_constructor_takes_an_explicit_model_and_base_url()
    {
        var s = new LlmSettings("meta", Key, "m-1", "https://meta.example.com/v9");
        Assert.Equal("m-1", s.Model);
        Assert.Equal("https://meta.example.com/v9", s.BaseUrl);
    }

    [Fact]
    public void S6_the_constructor_applies_the_same_validation()
    {
        Assert.Throws<LlmConfigurationException>(() => new LlmSettings("nope", Key));
        Assert.Throws<LlmConfigurationException>(() => new LlmSettings("openai_compatible", Key));
        Assert.Equal("SENSEI_LLM_API_KEY is not set", Assert.Throws<LlmConfigurationException>(() => new LlmSettings("meta", " ")).Message);
    }

    // ---------------------------------------------------------------- S79 base URL

    [Theory]
    [InlineData("https://llm.example.com/v1")]
    [InlineData("https://llm.example.com")]
    [InlineData("https://llm.example.com:8443/api/v1")]
    [InlineData("http://localhost:8080/v1")]
    [InlineData("http://127.0.0.1:9999/v1")]
    [InlineData("http://[::1]:9999/v1")]
    [InlineData("http://LOCALHOST/v1")]
    public void S79_accepted_base_urls(string url)
    {
        var s = new LlmSettings("openai_compatible", Key, null, url);
        Assert.Equal(url, s.BaseUrl);
    }

    [Theory]
    [InlineData("http://llm.example.com/v1")]
    [InlineData("http://192.168.1.10:8080/v1")]
    [InlineData("http://localhost.example.com/v1")]
    [InlineData("ftp://llm.example.com/v1")]
    [InlineData("llm.example.com/v1")]
    [InlineData("/v1")]
    [InlineData("https://user:pass@llm.example.com/v1")]
    [InlineData("https://user@llm.example.com/v1")]
    [InlineData("https://llm.example.com/v1?key=abc")]
    [InlineData("https://llm.example.com/v1?")]
    [InlineData("https://llm.example.com/v1#frag")]
    [InlineData("https:///v1")]
    public void S79_rejected_base_urls(string url)
    {
        Assert.Throws<LlmConfigurationException>(() => new LlmSettings("openai_compatible", Key, null, url));
    }

    [Fact]
    public void S79_a_rejected_url_applies_to_the_environment_override_of_a_preset_too()
    {
        Assert.Throws<LlmConfigurationException>(() =>
            LlmSettings.FromEnvironment(Env(("SENSEI_LLM_API_KEY", Key), ("SENSEI_LLM_BASE_URL", "http://example.com/v1"))));
    }

    [Fact]
    public void S79_validation_order_is_provider_then_base_url_then_key()
    {
        // everything wrong: the provider is reported
        var provider = Assert.Throws<LlmConfigurationException>(() => new LlmSettings("nope", "", null, "http://example.com"));
        Assert.Contains("allowed values", provider.Message);
        // provider fine, URL and key wrong: the URL is reported
        var url = Assert.Throws<LlmConfigurationException>(() => new LlmSettings("meta", "", null, "http://example.com"));
        Assert.Contains("SENSEI_LLM_BASE_URL", url.Message);
        // provider and URL fine, key missing: the key is reported
        var key = Assert.Throws<LlmConfigurationException>(() => new LlmSettings("meta", "", null, "https://example.com"));
        Assert.Equal("SENSEI_LLM_API_KEY is not set", key.Message);
        // openai_compatible with nothing: the missing URL comes before the missing key
        var missingUrl = Assert.Throws<LlmConfigurationException>(() => new LlmSettings("openai_compatible", ""));
        Assert.Contains("SENSEI_LLM_BASE_URL", missingUrl.Message);
    }

    [Fact]
    public void S79_exception_messages_never_contain_the_key_or_the_rejected_url()
    {
        var error = Assert.Throws<LlmConfigurationException>(() => new LlmSettings("openai_compatible", Key, null, "https://user:" + Key + "@llm.example.com/v1"));
        Assert.DoesNotContain(Key, error.Message);
        Assert.DoesNotContain("llm.example.com", error.Message);
    }

    [Fact]
    public void S79_no_client_is_created_for_invalid_settings()
    {
        // The only way to get a client is through valid settings: the constructor refuses first.
        Assert.Throws<LlmConfigurationException>(() => ChatClientFactory.Create(new LlmSettings("openai_compatible", Key, null, "http://example.com")));
    }
}

public sealed class ChatClientFactoryTests
{
    private const string Key = "factory-key-0123456789";

    [Fact]
    public async Task S6_create_returns_a_chat_client_that_posts_to_the_base_url_with_model_and_bearer_key()
    {
        using var server = new FakeChatServer().EnqueueText("hello from the fake");
        var settings = new LlmSettings("openai_compatible", Key, "vendor/model-7", server.BaseUrl);
        using IChatClient client = ChatClientFactory.Create(settings);

        var response = await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") });

        Assert.Equal("hello from the fake", response.Text);
        var request = Assert.Single(server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/v1/chat/completions", request.Path);
        Assert.Equal("Bearer " + Key, request.Authorization);
        Assert.Equal("vendor/model-7", request.Json.GetProperty("model").GetString());
    }

    [Fact]
    public async Task S6_create_reports_usage_from_the_provider_response()
    {
        using var server = new FakeChatServer().EnqueueText("x", promptTokens: 21, completionTokens: 4);
        using IChatClient client = ChatClientFactory.Create(new LlmSettings("openai_compatible", Key, null, server.BaseUrl));
        var response = await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") });
        Assert.Equal(21, response.Usage!.InputTokenCount);
        Assert.Equal(4, response.Usage.OutputTokenCount);
    }

    [Fact]
    public void S6_create_returns_the_microsoft_extensions_ai_chat_client_interface()
    {
        using IChatClient client = ChatClientFactory.Create(new LlmSettings("openrouter", Key));
        Assert.NotNull(client);
        Assert.NotNull(client.GetService(typeof(ChatClientMetadata)));
    }

    [Fact]
    public void S6_create_rejects_null_settings()
    {
        Assert.Throws<ArgumentNullException>(() => ChatClientFactory.Create(null!));
    }
}

using Sensei.Engine.Llm;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Integration;

/// <summary>S126: exact-value redaction applies only to values of 8 or more characters.</summary>
public sealed class S126RedactionLengthTests
{
    [Theory]
    [InlineData("k")]
    [InlineData("k-1")]
    [InlineData("abcdefg")]
    public void S126_a_known_secret_shorter_than_eight_characters_is_never_searched_for(string secret)
    {
        var secrets = new Secrets(new[] { secret });
        const string text = "provider=openrouter model=meta/muse-spark-1.3-contributor abcdefg";
        Assert.Equal(text, secrets.Sanitize(text));
        Assert.False(secrets.Check(text));
    }

    [Fact]
    public void S126_a_known_secret_of_exactly_eight_characters_is_redacted_and_checked()
    {
        var secrets = new Secrets(new[] { "abcdefgh" });
        Assert.Equal("a [redacted] b", secrets.Sanitize("a abcdefgh b"));
        Assert.True(secrets.Check("a abcdefgh b"));
    }

    [Fact]
    public void S126_a_longer_secret_still_wins_when_a_short_one_is_listed_with_it()
    {
        var secrets = new Secrets(new[] { "ab", "hunter-example-value" });
        Assert.Equal("ab and [redacted]", secrets.Sanitize("ab and hunter-example-value"));
    }

    [Theory]
    [InlineData("k-1")]
    [InlineData("rk")]
    [InlineData("a")]
    public void S126_settings_to_string_keeps_the_model_name_and_still_hides_a_short_key(string key)
    {
        var settings = new LlmSettings("openrouter", key);
        Assert.Equal(
            "provider=openrouter model=meta/muse-spark-1.3-contributor base_url=https://openrouter.ai/api/v1 reasoning_effort=minimal max_output_tokens=2000 api_key=[set]",
            settings.ToString());
    }

    [Fact]
    public void S126_settings_to_string_redacts_a_key_of_eight_characters_from_other_fields()
    {
        var settings = new LlmSettings("openai_compatible", "abcdefgh", "model-abcdefgh", "https://example.com/v1");
        var text = settings.ToString();
        Assert.DoesNotContain("abcdefgh", text);
        Assert.Contains("api_key=[set]", text);
    }
}

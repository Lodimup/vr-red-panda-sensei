using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sensei.Engine.Llm;

/// <summary>Thrown when the LLM provider settings are missing or invalid (S4, S79).</summary>
public sealed class LlmConfigurationException : Exception
{
    public LlmConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// LLM provider settings (SPEC §2, S4–S6, S79): provider, bearer key, model and base URL, validated in that order
/// (provider, base URL, key) before any client is created.
/// </summary>
public sealed class LlmSettings
{
    public const string ProviderVariable = "SENSEI_LLM_PROVIDER";
    public const string ApiKeyVariable = "SENSEI_LLM_API_KEY";
    public const string ModelVariable = "SENSEI_LLM_MODEL";
    public const string BaseUrlVariable = "SENSEI_LLM_BASE_URL";
    public const string ReasoningEffortVariable = "SENSEI_LLM_REASONING_EFFORT";
    public const string MaxOutputTokensVariable = "SENSEI_LLM_MAX_OUTPUT_TOKENS";

    public const string OpenRouter = "openrouter";
    public const string Meta = "meta";
    public const string OpenAiCompatible = "openai_compatible";

    public const string DefaultModel = "meta/muse-spark-1.3-contributor";

    /// <summary>Lowest effort the default model accepts; none is rejected by the provider (HTTP 400, reasoning mandatory).</summary>
    public const string DefaultReasoningEffort = "minimal";

    /// <summary>S125: default bound on the tokens of one response.</summary>
    public const int DefaultMaxOutputTokens = 2000;

    /// <summary>Allowed provider names, in the order the SPEC lists them.</summary>
    public static IReadOnlyList<string> Providers { get; } = new[] { OpenRouter, Meta, OpenAiCompatible };

    /// <summary>S125: allowed reasoning effort values, lowest first.</summary>
    public static IReadOnlyList<string> ReasoningEfforts { get; } = new[] { "none", "minimal", "low", "medium", "high" };

    /// <summary>The S6 four-argument form, kept so callers built against it still bind; S125 values take their defaults.</summary>
    public LlmSettings(string provider, string apiKey, string? model, string? baseUrl)
        : this(provider, apiKey, model, baseUrl, null, null)
    {
    }

    /// <summary>
    /// Builds settings from explicit values (what Unity does with the player's key). Applies the same validation and
    /// defaults as <see cref="FromEnvironment"/>: provider, then base URL, then key (S79).
    /// </summary>
    /// <param name="provider"><c>openrouter</c>, <c>meta</c> or <c>openai_compatible</c>.</param>
    /// <param name="apiKey">Bearer key.</param>
    /// <param name="model">Model id; the SPEC default when null or empty.</param>
    /// <param name="baseUrl">Override base URL; required for <c>openai_compatible</c>.</param>
    /// <param name="reasoningEffort">S125: <c>none</c>, <c>minimal</c>, <c>low</c>, <c>medium</c> or <c>high</c>; <c>minimal</c> when null or empty.</param>
    /// <param name="maxOutputTokens">S125: bound on each response; 2000 when null. Must be positive.</param>
    public LlmSettings(string provider, string apiKey, string? model = null, string? baseUrl = null, string? reasoningEffort = null, int? maxOutputTokens = null)
    {
        var p = Clean(provider);
        if (p is null || Array.IndexOf(Providers.ToArrayCompat(), p) < 0)
            throw new LlmConfigurationException($"unknown {ProviderVariable} '{Describe(p, apiKey)}'; allowed values: {string.Join(", ", Providers)}");

        var url = Clean(baseUrl) ?? PresetUrl(p);
        if (url is null)
            throw new LlmConfigurationException($"{BaseUrlVariable} is required for {OpenAiCompatible}");
        if (!IsAcceptableBaseUrl(url))
            throw new LlmConfigurationException($"{BaseUrlVariable} must be an absolute https URL without user info, query or fragment (http is allowed only for localhost, 127.0.0.1 or [::1])");

        var key = Clean(apiKey);
        if (key is null)
            throw new LlmConfigurationException($"{ApiKeyVariable} is not set");

        var effort = Clean(reasoningEffort) ?? DefaultReasoningEffort;
        if (Array.IndexOf(ReasoningEfforts.ToArrayCompat(), effort) < 0)
            throw new LlmConfigurationException($"unknown {ReasoningEffortVariable} '{effort}'; allowed values: {string.Join(", ", ReasoningEfforts)}");

        var tokens = maxOutputTokens ?? DefaultMaxOutputTokens;
        if (tokens < 1)
            throw new LlmConfigurationException($"{MaxOutputTokensVariable} must be a positive whole number");

        Provider = p;
        ApiKey = key;
        Model = Clean(model) ?? DefaultModel;
        BaseUrl = url;
        ReasoningEffort = effort;
        MaxOutputTokens = tokens;
    }

    public string Provider { get; }

    public string Model { get; }

    public string BaseUrl { get; }

    /// <summary>S125: <c>none</c>, <c>low</c>, <c>medium</c> or <c>high</c>; sent with every request.</summary>
    public string ReasoningEffort { get; }

    /// <summary>S125: bound on the tokens of each response; sent with every request.</summary>
    public int MaxOutputTokens { get; }

    /// <summary>The bearer key. Internal on purpose: nothing outside the engine and the CLI needs to read it back (S5, S77).</summary>
    internal string ApiKey { get; }

    /// <summary>S4: applies the environment table. Values are trimmed; empty counts as missing.</summary>
    public static LlmSettings FromEnvironment(IReadOnlyDictionary<string, string?> env)
    {
        if (env is null) throw new ArgumentNullException(nameof(env));
        return new LlmSettings(
            Clean(Get(env, ProviderVariable)) ?? OpenRouter,
            Get(env, ApiKeyVariable) ?? "",
            Get(env, ModelVariable),
            Get(env, BaseUrlVariable),
            Get(env, ReasoningEffortVariable),
            ParseTokens(Get(env, MaxOutputTokensVariable)));
    }

    /// <summary>
    /// S125: empty means the default; text that is not a whole number becomes 0 so the constructor rejects it with the
    /// usual message, after the provider, base URL and key checks (S79 order).
    /// </summary>
    private static int? ParseTokens(string? text)
    {
        var clean = Clean(text);
        if (clean is null) return null;
        return int.TryParse(clean, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    /// <summary>
    /// S5, S125: <c>provider=&lt;p&gt; model=&lt;m&gt; base_url=&lt;u&gt; reasoning_effort=&lt;e&gt; max_output_tokens=&lt;n&gt; api_key=[set]</c>;
    /// the key never appears, in any field.
    /// </summary>
    public override string ToString() =>
        $"provider={Redact(Provider)} model={Redact(Model)} base_url={Redact(BaseUrl)} reasoning_effort={Redact(ReasoningEffort)} max_output_tokens={MaxOutputTokens.ToString(CultureInfo.InvariantCulture)} api_key=[set]";

    // S126: a key shorter than the minimum is not searched for inside other fields; api_key=[set] never prints it either way.
    private string Redact(string text) => ApiKey.Length < Trace.Secrets.MinimumKnownSecretLength ? text : text.Replace(ApiKey, "[redacted]");

    private static string? Get(IReadOnlyDictionary<string, string?> env, string name) =>
        env.TryGetValue(name, out var value) ? value : null;

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// The rejected provider value as it may appear in the message (S77): the supplied key and anything that looks like a credential is
    /// redacted first, then the text is cut to 40 characters, so a key typed into the wrong field never travels in an exception.
    /// </summary>
    private static string Describe(string? provider, string? apiKey)
    {
        var text = new Trace.Secrets(new[] { apiKey?.Trim() ?? "" }).Sanitize(provider ?? "") ?? "";
        return text.Length <= 40 ? text : text.Substring(0, 40);
    }

    private static string? PresetUrl(string provider) => provider switch
    {
        OpenRouter => "https://openrouter.ai/api/v1",
        Meta => "https://api.meta.ai/v1",
        _ => null,
    };

    /// <summary>S79: absolute https URL without user info, query or fragment; http only for loopback hosts.</summary>
    internal static bool IsAcceptableBaseUrl(string url)
    {
        if (url.IndexOf('?') >= 0 || url.IndexOf('#') >= 0) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.UserInfo.Length != 0) return false;
        if (uri.Host.Length == 0) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        if (uri.Scheme != Uri.UriSchemeHttp) return false;
        var host = uri.Host.ToLower(CultureInfo.InvariantCulture);
        return host == "localhost" || host == "127.0.0.1" || host == "[::1]" || host == "::1";
    }
}

internal static class ReadOnlyListExtensions
{
    public static string[] ToArrayCompat(this IReadOnlyList<string> list)
    {
        var array = new string[list.Count];
        for (var i = 0; i < array.Length; i++) array[i] = list[i];
        return array;
    }
}

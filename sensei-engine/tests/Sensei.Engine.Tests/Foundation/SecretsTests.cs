using Sensei.Engine.Learner;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Foundation;

public sealed class SecretsTests
{
    private static string Sk(int tail) => "sk-" + new string('a', tail);

    // ------------------------------------------------------------------ S78: sk- keys

    [Fact]
    public void S78_sk_key_with_16_chars_is_a_credential()
    {
        Assert.True(CredentialPattern.IsMatch("sk-abcdefghijklmnop"));
        Assert.True(CredentialPattern.IsMatch(Sk(16)));
    }

    [Fact]
    public void S78_sk_key_with_15_chars_is_not_a_credential()
    {
        Assert.False(CredentialPattern.IsMatch(Sk(15)));
        Assert.False(CredentialPattern.IsMatch("sk-abcdefghijklmno"));
    }

    [Fact]
    public void S78_sk_key_with_more_than_16_chars_is_a_credential()
    {
        Assert.True(CredentialPattern.IsMatch(Sk(17)));
        Assert.True(CredentialPattern.IsMatch(Sk(60)));
    }

    [Fact]
    public void S78_sk_key_is_case_insensitive()
    {
        Assert.True(CredentialPattern.IsMatch("SK-ABCDEFGHIJKLMNOP"));
        Assert.True(CredentialPattern.IsMatch("Sk-AbCdEfGhIjKlMnOp"));
        Assert.False(CredentialPattern.IsMatch("SK-ABCDEFGHIJKLMNO"));
    }

    [Fact]
    public void S78_sk_key_may_contain_digits_underscore_and_hyphen()
    {
        Assert.True(CredentialPattern.IsMatch("sk-ab_cd-ef12_gh-34"));
        Assert.True(CredentialPattern.IsMatch("sk-1234567890123456"));
        Assert.True(CredentialPattern.IsMatch("sk-" + new string('_', 16)));
        Assert.True(CredentialPattern.IsMatch("sk-" + new string('-', 16)));
    }

    [Fact]
    public void S78_sk_key_inside_a_sentence_is_a_credential()
    {
        Assert.True(CredentialPattern.IsMatch("my key is sk-abcdefghijklmnop please remember it"));
    }

    [Fact]
    public void S78_sk_key_chars_must_be_contiguous()
    {
        Assert.False(CredentialPattern.IsMatch("sk-abcdefgh ijklmnop"));
        Assert.False(CredentialPattern.IsMatch("sk-abcdefgh.ijklmnop"));
    }

    [Fact]
    public void S78_prefix_alone_or_a_different_prefix_is_not_a_credential()
    {
        Assert.False(CredentialPattern.IsMatch("sk-"));
        Assert.False(CredentialPattern.IsMatch("sk"));
        Assert.False(CredentialPattern.IsMatch("sx-abcdefghijklmnop"));
    }

    // ------------------------------------------------------------------ S78: assignments

    [Fact]
    public void S78_password_assignment_with_6_chars_is_a_credential()
    {
        Assert.True(CredentialPattern.IsMatch("password: abcdef"));
    }

    [Fact]
    public void S78_password_assignment_with_5_chars_is_not_a_credential()
    {
        Assert.False(CredentialPattern.IsMatch("password: abcde"));
    }

    [Fact]
    public void S78_assignment_value_longer_than_6_chars_is_a_credential()
    {
        Assert.True(CredentialPattern.IsMatch("password: abcdefghij"));
    }

    [Fact]
    public void S78_assignment_value_cannot_be_split_by_whitespace_to_reach_6_chars()
    {
        Assert.False(CredentialPattern.IsMatch("password: abc def"));
        Assert.False(CredentialPattern.IsMatch("password: abcde fg"));
    }

    [Theory]
    [InlineData("api_key=abcdef")]
    [InlineData("api_key: abcdef")]
    [InlineData("api-key = abcdef")]
    [InlineData("api-key=abcdef")]
    [InlineData("apikey:abcdef")]
    [InlineData("apikey: abcdef")]
    [InlineData("apikey = abcdef")]
    [InlineData("password=abcdef")]
    [InlineData("password : abcdef")]
    [InlineData("passwd: abcdef")]
    [InlineData("passwd=abcdef")]
    [InlineData("token: abcdef")]
    [InlineData("token=abcdef")]
    [InlineData("secret: abcdef")]
    [InlineData("secret=abcdef")]
    [InlineData("secret =   abcdef")]
    [InlineData("PASSWORD: ABCDEF")]
    [InlineData("Api_Key=ABCdef")]
    [InlineData("my password: abcdef ok")]
    public void S78_assignment_forms_with_6_char_value_are_credentials(string text)
    {
        Assert.True(CredentialPattern.IsMatch(text), text);
    }

    [Theory]
    [InlineData("api_key=abcde")]
    [InlineData("api_key: abcde")]
    [InlineData("api-key = abcde")]
    [InlineData("apikey:abcde")]
    [InlineData("password=abcde")]
    [InlineData("passwd: abcde")]
    [InlineData("token: abcde")]
    [InlineData("token=abcde")]
    [InlineData("secret: abcde")]
    [InlineData("secret =   abcde")]
    [InlineData("PASSWORD: ABCDE")]
    public void S78_assignment_forms_with_5_char_value_are_not_credentials(string text)
    {
        Assert.False(CredentialPattern.IsMatch(text), text);
    }

    [Theory]
    [InlineData("password abcdefgh")]
    [InlineData("password is abcdefgh")]
    [InlineData("token abcdefgh")]
    [InlineData("secret-abcdefgh")]
    [InlineData("api key abcdefgh")]
    [InlineData("api key: abcdefgh")]
    [InlineData("key=abcdefgh")]
    [InlineData("name: abcdefgh")]
    public void S78_assignment_needs_a_listed_name_and_a_colon_or_equals(string text)
    {
        Assert.False(CredentialPattern.IsMatch(text), text);
    }

    // ------------------------------------------------------------------ S78: long mixed runs

    [Fact]
    public void S78_32_char_run_with_a_letter_and_a_digit_is_a_credential()
    {
        var run = "abcdefghijklmnopqrstuvwxyz012345";
        Assert.Equal(32, run.Length);
        Assert.True(CredentialPattern.IsMatch(run));
    }

    [Fact]
    public void S78_31_char_run_with_a_letter_and_a_digit_is_not_a_credential()
    {
        var run = "abcdefghijklmnopqrstuvwxyz01234";
        Assert.Equal(31, run.Length);
        Assert.False(CredentialPattern.IsMatch(run));
    }

    [Fact]
    public void S78_long_run_longer_than_32_with_letter_and_digit_is_a_credential()
    {
        Assert.True(CredentialPattern.IsMatch(new string('a', 60) + "7"));
        Assert.True(CredentialPattern.IsMatch("5" + new string('Z', 80)));
    }

    [Fact]
    public void S78_32_char_run_may_include_the_symbol_characters()
    {
        var run = "Ab1+/_=-Ab1+/_=-Ab1+/_=-Ab1+/_=-";
        Assert.Equal(32, run.Length);
        Assert.True(CredentialPattern.IsMatch(run));
    }

    [Fact]
    public void S78_32_char_run_with_a_mid_run_symbol_is_still_one_run()
    {
        var run = new string('a', 15) + "-" + new string('b', 15) + "9";
        Assert.Equal(32, run.Length);
        Assert.True(CredentialPattern.IsMatch(run));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    public void S78_long_letters_only_run_is_not_a_credential(int length)
    {
        Assert.False(CredentialPattern.IsMatch(new string('a', length)));
        Assert.False(CredentialPattern.IsMatch(new string('Q', length)));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    public void S78_long_digits_only_run_is_not_a_credential(int length)
    {
        Assert.False(CredentialPattern.IsMatch(new string('7', length)));
    }

    [Fact]
    public void S78_long_run_of_letters_and_symbols_without_a_digit_is_not_a_credential()
    {
        var run = string.Concat(Enumerable.Repeat("a-b+", 10));
        Assert.True(run.Length >= 32);
        Assert.False(CredentialPattern.IsMatch(run));
    }

    [Fact]
    public void S78_letters_run_next_to_a_separate_digit_is_not_one_run()
    {
        Assert.False(CredentialPattern.IsMatch(new string('a', 40) + " 9"));
        Assert.False(CredentialPattern.IsMatch("9 " + new string('a', 40)));
    }

    [Fact]
    public void S78_two_runs_each_under_32_are_not_a_credential_even_when_adjacent_with_a_space()
    {
        var half = new string('a', 15) + "1";
        Assert.False(CredentialPattern.IsMatch(half + " " + half));
    }

    [Fact]
    public void S78_mixed_run_is_found_inside_surrounding_text()
    {
        var run = "abcdefghijklmnopqrstuvwxyz012345";
        Assert.True(CredentialPattern.IsMatch("here is my token " + run + " thanks"));
    }

    // ------------------------------------------------------------------ S78: ordinary words

    [Theory]
    [InlineData("password")]
    [InlineData("API key")]
    [InlineData("antidisestablishmentarianism")]
    [InlineData("今日はpasswordという単語を練習したい")]
    public void S78_ordinary_words_are_not_credentials(string text)
    {
        Assert.False(CredentialPattern.IsMatch(text));
        Assert.False(Secrets.None.Check(text));
        Assert.Equal(text, Secrets.None.Sanitize(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("おちゃをください")]
    [InlineData("I would like some tea, please.")]
    public void S78_everyday_learner_text_is_not_a_credential(string text)
    {
        Assert.False(CredentialPattern.IsMatch(text));
    }

    [Fact]
    public void S78_IsMatch_is_false_for_null_and_empty()
    {
        Assert.False(CredentialPattern.IsMatch(null));
        Assert.False(CredentialPattern.IsMatch(""));
    }

    // ------------------------------------------------------------------ S77: Redact

    [Fact]
    public void S77_Redact_replaces_an_sk_key_and_keeps_the_text_around_it()
    {
        Assert.Equal("my key is [redacted] ok", CredentialPattern.Redact("my key is sk-abcdefghijklmnop ok"));
    }

    [Fact]
    public void S77_Redact_leaves_a_15_char_sk_key_alone()
    {
        var text = "my key is " + Sk(15) + " ok";
        Assert.Equal(text, CredentialPattern.Redact(text));
    }

    [Fact]
    public void S77_Redact_replaces_an_assignment_including_the_name()
    {
        Assert.Equal("[redacted] ok", CredentialPattern.Redact("password: abcdef ok"));
    }

    [Fact]
    public void S77_Redact_leaves_a_5_char_assignment_alone()
    {
        Assert.Equal("password: abcde", CredentialPattern.Redact("password: abcde"));
    }

    [Fact]
    public void S77_Redact_replaces_a_mixed_long_run_but_not_a_letters_only_run()
    {
        var mixed = "abcdefghijklmnopqrstuvwxyz012345";
        var letters = new string('x', 40);
        Assert.Equal("a [redacted] b", CredentialPattern.Redact("a " + mixed + " b"));
        Assert.Equal("a " + letters + " b", CredentialPattern.Redact("a " + letters + " b"));
    }

    [Fact]
    public void S77_Redact_handles_several_matches_in_one_string()
    {
        var text = "one sk-abcdefghijklmnop two password: abcdef three";
        Assert.Equal("one [redacted] two [redacted] three", CredentialPattern.Redact(text));
    }

    [Fact]
    public void S77_Redact_returns_null_and_empty_unchanged()
    {
        Assert.Equal("", CredentialPattern.Redact(""));
        Assert.Null(CredentialPattern.Redact(null!));
    }

    [Fact]
    public void S77_redacted_marker_is_the_documented_text()
    {
        Assert.Equal("[redacted]", CredentialPattern.Redacted);
    }

    // ------------------------------------------------------------------ S77: Secrets

    [Fact]
    public void S77_Check_detects_a_known_secret()
    {
        var secrets = new Secrets(new[] { "hunter-example-value" });
        Assert.True(secrets.Check("hunter-example-value"));
        Assert.True(secrets.Check("please remember hunter-example-value for me"));
    }

    [Fact]
    public void S77_Check_does_not_flag_text_without_the_known_secret()
    {
        var secrets = new Secrets(new[] { "hunter-example-value" });
        Assert.False(secrets.Check("hello there"));
        Assert.False(secrets.Check("hunter-example-valu"));
        Assert.False(secrets.Check("hunter"));
    }

    [Fact]
    public void S77_known_secret_match_is_exact_not_case_folded()
    {
        var secrets = new Secrets(new[] { "hunter-example-value" });
        Assert.False(secrets.Check("HUNTER-EXAMPLE-VALUE"));
    }

    [Fact]
    public void S77_Check_still_applies_the_credential_pattern_with_known_secrets_set()
    {
        var secrets = new Secrets(new[] { "hunter-example-value" });
        Assert.True(secrets.Check("sk-abcdefghijklmnop"));
        Assert.False(secrets.Check(Sk(15)));
    }

    [Fact]
    public void S77_Check_with_no_known_secrets_still_applies_the_credential_pattern()
    {
        Assert.True(Secrets.None.Check("password: abcdef"));
        Assert.False(Secrets.None.Check("password: abcde"));
        Assert.True(new Secrets(null).Check("api_key=abcdef"));
        Assert.True(new Secrets(Array.Empty<string>()).Check("sk-abcdefghijklmnop"));
    }

    [Fact]
    public void S77_Check_is_false_for_null_and_empty()
    {
        var secrets = new Secrets(new[] { "hunter-example-value" });
        Assert.False(secrets.Check(null));
        Assert.False(secrets.Check(""));
    }

    [Fact]
    public void S77_Sanitize_replaces_every_occurrence_of_a_known_secret()
    {
        var secrets = new Secrets(new[] { "hunter-example-value" });
        Assert.Equal("a [redacted] b [redacted] c", secrets.Sanitize("a hunter-example-value b hunter-example-value c"));
    }

    [Fact]
    public void S77_Sanitize_leaves_text_without_secrets_unchanged()
    {
        var secrets = new Secrets(new[] { "hunter-example-value" });
        Assert.Equal("nothing to see here", secrets.Sanitize("nothing to see here"));
    }

    [Fact]
    public void S77_Sanitize_also_redacts_credential_pattern_matches()
    {
        var secrets = new Secrets(new[] { "hunter-example-value" });
        Assert.Equal("[redacted] and [redacted]", secrets.Sanitize("hunter-example-value and sk-abcdefghijklmnop"));
        Assert.Equal("x [redacted]", Secrets.None.Sanitize("x token: abcdefgh"));
    }

    [Fact]
    public void S77_Sanitize_with_no_known_secrets_redacts_pattern_matches()
    {
        Assert.Equal("use [redacted] now", Secrets.None.Sanitize("use sk-abcdefghijklmnop now"));
    }

    [Fact]
    public void S77_Sanitize_keeps_null_null_and_empty_empty()
    {
        var secrets = new Secrets(new[] { "hunter-example-value" });
        Assert.Null(secrets.Sanitize(null));
        Assert.Equal("", secrets.Sanitize(""));
    }

    [Fact]
    public void S77_empty_and_null_known_secrets_are_ignored()
    {
        var secrets = new Secrets(new[] { "", "hunter-example-value", null! });
        Assert.False(secrets.Check("ordinary text"));
        Assert.Equal("ordinary text", secrets.Sanitize("ordinary text"));
        Assert.True(secrets.Check("hunter-example-value"));
    }

    [Fact]
    public void S77_only_empty_known_secrets_leaves_nothing_flagged()
    {
        var secrets = new Secrets(new[] { "" });
        Assert.False(secrets.Check("anything at all"));
        Assert.Equal("anything at all", secrets.Sanitize("anything at all"));
    }

    [Fact]
    public void S77_duplicate_known_secrets_are_harmless()
    {
        var secrets = new Secrets(new[] { "hunter-example-value", "hunter-example-value" });
        Assert.Equal("[redacted]", secrets.Sanitize("hunter-example-value"));
    }

    [Fact]
    public void S77_longer_secret_is_redacted_before_a_shorter_overlapping_one_when_listed_short_first()
    {
        var secrets = new Secrets(new[] { "alpha", "alphabet" });
        Assert.Equal("[redacted] soup", secrets.Sanitize("alphabet soup"));
    }

    [Fact]
    public void S77_longer_secret_is_redacted_before_a_shorter_overlapping_one_when_listed_long_first()
    {
        var secrets = new Secrets(new[] { "alphabet", "alpha" });
        Assert.Equal("[redacted] soup", secrets.Sanitize("alphabet soup"));
    }

    [Fact]
    public void S77_shorter_secret_alone_does_not_hide_the_remaining_text()
    {
        // S126: both values are at least 8 characters, the shortest that is searched for.
        var secrets = new Secrets(new[] { "alpha-key", "alpha-key-long" });
        Assert.Equal("[redacted] and [redacted]", secrets.Sanitize("alpha-key and alpha-key-long"));
    }

    [Fact]
    public void S77_secret_that_is_a_suffix_of_a_longer_one_leaves_no_fragment()
    {
        var secrets = new Secrets(new[] { "beta", "alphabeta" });
        Assert.Equal("[redacted]", secrets.Sanitize("alphabeta"));
    }

    // ------------------------------------------------------------------ S80: hidden level

    [Theory]
    [InlineData("working level")]
    [InlineData("Working_Level")]
    [InlineData("WORKING LEVEL")]
    [InlineData("working_level")]
    [InlineData("Working Level")]
    [InlineData("your working level is n4")]
    [InlineData("Tell me the WORKING_LEVEL of the learner.")]
    [InlineData("xworking levelx")]
    [InlineData("That word is N5.")]
    [InlineData("JLPT N4 grammar")]
    [InlineData("pre-n3")]
    [InlineData("Ｎ5")]
    public void S80_working_level_text_is_refused(string text)
    {
        Assert.True(HiddenLevelGuard.Check(text), text);
    }

    [Theory]
    [InlineData("This is a beginner word.")]
    [InlineData("n5word")]
    [InlineData("working")]
    [InlineData("level")]
    [InlineData("the level is working")]
    [InlineData("working hard on your level")]
    [InlineData("workinglevel")]
    [InlineData("")]
    public void S80_other_text_is_allowed(string text)
    {
        Assert.False(HiddenLevelGuard.Check(text), text);
    }

    [Fact]
    public void S80_null_is_allowed()
    {
        Assert.False(HiddenLevelGuard.Check(null));
    }

    [Fact]
    public void S80_refusal_detail_is_hidden_level()
    {
        Assert.Equal("hidden_level", HiddenLevelGuard.Detail);
    }
}

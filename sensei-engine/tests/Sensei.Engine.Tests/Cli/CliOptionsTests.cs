using Sensei.Engine.Cli;

namespace Sensei.Engine.Tests.Cli;

public sealed class CliOptionsTests
{
    private static CliOptions Parse(params string[] args)
    {
        var options = CliOptions.Parse(args, out var error);
        Assert.Null(error);
        return Assert.IsType<CliOptions>(options);
    }

    private static string Fails(params string[] args)
    {
        Assert.Null(CliOptions.Parse(args, out var error));
        return Assert.IsType<string>(error);
    }

    [Fact]
    public void S13_defaults_are_the_spec_defaults()
    {
        var o = Parse();
        Assert.Equal("./.sensei", o.ProfileDir);
        Assert.Equal("default", o.ProfileId);
        Assert.Equal("console", o.Voice);
        Assert.Null(o.ScriptPath);
        Assert.False(o.Trace);
        Assert.False(o.Help);
    }

    [Fact]
    public void S13_every_option_is_read()
    {
        var o = Parse("--profile-dir", "/tmp/p", "--profile", "kid1", "--voice", "say", "--script", "lines.txt", "--trace");
        Assert.Equal("/tmp/p", o.ProfileDir);
        Assert.Equal("kid1", o.ProfileId);
        Assert.Equal("say", o.Voice);
        Assert.Equal("lines.txt", o.ScriptPath);
        Assert.True(o.Trace);
    }

    [Fact]
    public void S13_options_may_use_an_equals_sign()
    {
        var o = Parse("--profile=kid2", "--voice=console", "--profile-dir=/x");
        Assert.Equal("kid2", o.ProfileId);
        Assert.Equal("console", o.Voice);
        Assert.Equal("/x", o.ProfileDir);
    }

    [Fact]
    public void S13_help_is_recognised()
    {
        Assert.True(Parse("--help").Help);
        Assert.True(Parse("-h").Help);
    }

    [Theory]
    [InlineData("--profile")]
    [InlineData("--profile-dir")]
    [InlineData("--voice")]
    [InlineData("--script")]
    public void S13_an_option_without_a_value_is_an_error(string option)
    {
        Assert.Contains(option, Fails(option));
    }

    [Fact]
    public void S13_an_empty_value_is_an_error()
    {
        Assert.Contains("--profile", Fails("--profile="));
    }

    [Fact]
    public void S13_an_unknown_option_is_an_error_naming_it()
    {
        Assert.Contains("--nope", Fails("--nope"));
    }

    [Fact]
    public void S13_a_voice_other_than_console_or_say_is_an_error()
    {
        Assert.Contains("bogus", Fails("--voice", "bogus"));
    }
}

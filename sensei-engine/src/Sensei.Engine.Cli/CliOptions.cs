namespace Sensei.Engine.Cli;

/// <summary>Command-line options (SPEC §13).</summary>
internal sealed class CliOptions
{
    public const string Usage =
        "usage: sensei [--profile-dir <dir>] [--profile <id>] [--voice console|say] [--script <file>] [--trace]\n" +
        "  --profile-dir <dir>   profile and trace directory (default ./.sensei)\n" +
        "  --profile <id>        profile id (default: default)\n" +
        "  --voice console|say   console prints speech; say also speaks it with macOS say (default: console)\n" +
        "  --script <file>       run the lines of the file as if typed, then exit\n" +
        "  --trace               print one line per tool call\n" +
        "  --help                show this text";

    public string ProfileDir { get; private set; } = "./.sensei";
    public string ProfileId { get; private set; } = "default";
    public string Voice { get; private set; } = "console";
    public string? ScriptPath { get; private set; }
    public bool Trace { get; private set; }
    public bool Help { get; private set; }

    /// <summary>Parses <paramref name="args"/>; returns null and sets <paramref name="error"/> when they are invalid.</summary>
    public static CliOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        var options = new CliOptions();
        error = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? inline = null;
            var eq = arg.IndexOf('=');
            if (arg.StartsWith("--", StringComparison.Ordinal) && eq > 0)
            {
                inline = arg[(eq + 1)..];
                arg = arg[..eq];
            }

            switch (arg)
            {
                case "--trace":
                    options.Trace = true;
                    break;
                case "--help":
                case "-h":
                    options.Help = true;
                    break;
                case "--profile-dir":
                case "--profile":
                case "--voice":
                case "--script":
                    string? value = inline;
                    if (value is null)
                    {
                        if (i + 1 >= args.Count)
                        {
                            error = $"option {arg} needs a value";
                            return null;
                        }
                        value = args[++i];
                    }
                    if (value.Length == 0)
                    {
                        error = $"option {arg} needs a value";
                        return null;
                    }
                    if (arg == "--profile-dir") options.ProfileDir = value;
                    else if (arg == "--profile") options.ProfileId = value;
                    else if (arg == "--script") options.ScriptPath = value;
                    else
                    {
                        if (value != "console" && value != "say")
                        {
                            error = $"--voice must be console or say, not '{value}'";
                            return null;
                        }
                        options.Voice = value;
                    }
                    break;
                default:
                    error = $"unknown option '{arg}'";
                    return null;
            }
        }
        return options;
    }
}

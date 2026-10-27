using System.Text;
using Microsoft.Extensions.AI;
using Sensei.Engine.Cli;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Cli;

/// <summary>What one in-process run of the CLI printed and returned.</summary>
public sealed record CliResult(int Code, string Text, string ProfileDir)
{
    public IReadOnlyList<string> Lines => Text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public IEnumerable<string> ResultLines => Lines.Where(l => l.StartsWith("Sensei: ", StringComparison.Ordinal));

    public int IndexOf(Func<string, bool> predicate) => Lines.ToList().FindIndex(l => predicate(l));
}

/// <summary>A chat client driven by a function, for tests that need to answer by content rather than by queue order.</summary>
internal sealed class FuncChatClient : IChatClient
{
    private readonly Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> _respond;

    public FuncChatClient(Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> respond)
    {
        _respond = respond;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        _respond(messages.ToList(), options, cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates()) yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}

internal static class InProcessCli
{
    public static IReadOnlyDictionary<string, string?> NoEnv { get; } = new Dictionary<string, string?>();

    public static async Task<CliResult> ScriptAsync(string[] lines, IChatClient? chat = null, IReadOnlyDictionary<string, string?>? env = null,
        string[]? extra = null, string? profileDir = null)
    {
        profileDir ??= CliRunner.NewTempDirectory();
        var script = Path.Combine(CliRunner.NewTempDirectory(), "script.txt");
        await File.WriteAllLinesAsync(script, lines, new UTF8Encoding(false));
        var args = new List<string> { "--profile-dir", profileDir, "--script", script };
        if (extra is not null) args.AddRange(extra);
        var writer = new StringWriter();
        var code = await CliApp.RunAsync(args.ToArray(), TextReader.Null, writer, env ?? NoEnv, default, chat);
        return new CliResult(code, writer.ToString(), profileDir);
    }

    public static async Task<CliResult> InteractiveAsync(TextReader input, IChatClient? chat = null, IReadOnlyDictionary<string, string?>? env = null,
        string[]? extra = null, string? profileDir = null)
    {
        profileDir ??= CliRunner.NewTempDirectory();
        var args = new List<string> { "--profile-dir", profileDir };
        if (extra is not null) args.AddRange(extra);
        var writer = new StringWriter();
        var code = await CliApp.RunAsync(args.ToArray(), input, writer, env ?? NoEnv, default, chat);
        return new CliResult(code, writer.ToString(), profileDir);
    }
}


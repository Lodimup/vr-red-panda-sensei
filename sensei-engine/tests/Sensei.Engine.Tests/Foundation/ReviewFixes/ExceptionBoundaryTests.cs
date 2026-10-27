using System.Collections;
using System.Text.RegularExpressions;
using Sensei.Engine.Learner;
using Sensei.Engine.Storage;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>
/// Review round 2, finding 1 and 2 at the cause: every public member of <see cref="SenseiEngine"/> is one exception boundary
/// (S77). Whatever a dependency throws — however deep, whatever its type, whichever entry point — the host receives a
/// <see cref="SenseiEngineException"/> or a documented exception with a sanitized message and no inner exception.
/// </summary>
public sealed class ExceptionBoundaryTests
{
    private const string Secret = "fictional-boundary-credential";

    /// <summary>A dependency exception whose every member carries the secret.</summary>
    private sealed class LeakyException : Exception
    {
        public LeakyException(Exception? inner)
            : base("leaky dependency: " + Secret, inner)
        {
            Data["credential"] = Secret;
            HelpLink = "https://example.com/help?token=" + Secret;
            Source = "driver-" + Secret;
        }

        public string Detail => "extra member " + Secret;
    }

    private sealed class DerivedInvalidOperation : InvalidOperationException
    {
        public DerivedInvalidOperation(string message)
            : base(message)
        {
        }
    }

    private sealed class HostileMessageException : Exception
    {
        public override string Message => throw new InvalidOperationException("message getter failed: " + Secret);
    }

    /// <summary>Runs a turn that blocks; its generation callback throws, so cancelling the generation throws.</summary>
    private sealed class CallbackRunner : IAgentRunner
    {
        private readonly EngineCore _core;

        public CallbackRunner(EngineCore core)
        {
            _core = core;
        }

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct)
        {
            using var registration = _core.GenerationToken.Register(() => throw new IOException("generation callback failed: " + Secret));
            Started.TrySetResult(true);
            await Release.Task;
            return new TurnResult { TurnId = request.Turn.TurnId, GenerationId = request.Turn.GenerationId, Status = TurnStatuses.Cancelled };
        }

        public void ResetConversation()
        {
        }
    }

    private sealed class ThrowingRunner : IAgentRunner
    {
        public Task<TurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct) =>
            throw new LeakyException(new IOException("provider socket: " + Secret));

        public void ResetConversation()
        {
        }
    }

    private sealed class ThrowingTool : ProbeTool
    {
        public ThrowingTool() : base("test_throw_leaky")
        {
        }

        public override Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct) =>
            throw new LeakyException(new IOException("tool socket: " + Secret));
    }

    private static void AssertBoundaryException(Exception? error)
    {
        Assert.NotNull(error);
        Assert.Null(error.InnerException);
        Assert.Empty(error.Data);
        Assert.DoesNotContain(Secret, error.Message);
        Assert.DoesNotContain(Secret, error.ToString());
        if (error is SenseiEngineException engine) Assert.DoesNotContain(Secret, engine.OriginalType);
    }

    // ------------------------------------------------------------------ a dependency exception, every member of it

    [Fact]
    public async Task S77_a_dependency_exception_leaves_as_one_SenseiEngineException_with_no_inner_data_or_members()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Store.FailSavesWith = new LeakyException(new IOException("middle", new LeakyException(null)));

        var error = await Record.ExceptionAsync(() => t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = false }));

        var safe = Assert.IsType<SenseiEngineException>(error);
        AssertBoundaryException(safe);
        Assert.Equal(typeof(LeakyException).FullName, safe.OriginalType);
        Assert.Contains("[redacted]", safe.Message);
    }

    // ------------------------------------------------------------------ every entry point

    public static IEnumerable<object[]> EntryPoints() => new[]
    {
        new object[] { "create" },
        new object[] { "set_preference" },
        new object[] { "set_starting_band" },
        new object[] { "reset" },
        new object[] { "run_turn" },
        new object[] { "correct_transcript" },
        new object[] { "invoke_tool" },
        new object[] { "cancel_generation" },
        new object[] { "begin_turn" },
        new object[] { "next_turn_cancels_running_turn" },
        new object[] { "reset_cancels_running_turn" },
        new object[] { "dispose_cancels_running_turn" },
    };

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public async Task S77_every_entry_point_translates_what_its_dependencies_throw(string entryPoint)
    {
        var secrets = new[] { Secret };
        Exception? error;
        switch (entryPoint)
        {
            case "create":
            {
                var store = new InMemoryProfileStore { FailSavesWith = new LeakyException(new IOException("store socket: " + Secret)) };
                error = await Record.ExceptionAsync(() => TestEngine.CreateAsync(store: store, knownSecrets: secrets));
                break;
            }
            case "set_preference":
            {
                await using var t = await TestEngine.CreateAsync(knownSecrets: secrets);
                t.Store.FailSavesWith = new LeakyException(new IOException(Secret));
                error = await Record.ExceptionAsync(() => t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = false }));
                break;
            }
            case "set_starting_band":
            {
                await using var t = await TestEngine.CreateAsync(knownSecrets: secrets);
                t.Store.FailSavesWith = new LeakyException(new IOException(Secret));
                error = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));
                break;
            }
            case "reset":
            {
                await using var t = await TestEngine.CreateAsync(knownSecrets: secrets);
                t.Store.FailSavesWith = new LeakyException(new IOException(Secret));
                error = await Record.ExceptionAsync(() => t.Engine.ResetProfileAsync());
                break;
            }
            case "run_turn":
            {
                await using var t = await TestEngine.CreateCoreAsync(new ScriptedChatClient(), null, null, null, null, null, "default", secrets, null, null, _ => new ThrowingRunner());
                error = await Record.ExceptionAsync(() => t.Engine.RunTurnAsync("hello"));
                break;
            }
            case "correct_transcript":
            {
                await using var t = await TestEngine.CreateAsync(knownSecrets: secrets);
                t.Engine.BeginTurn("hello");
                t.Store.FailSavesWith = new LeakyException(new IOException(Secret));
                error = await Record.ExceptionAsync(() => t.Engine.CorrectLastTranscriptAsync("hi"));
                break;
            }
            case "invoke_tool":
            {
                var tool = new ThrowingTool();
                await using var t = await TestEngine.CreateAsync(knownSecrets: secrets, extraTools: new SenseiTool[] { tool });
                t.Engine.BeginTurn("x");
                error = await Record.ExceptionAsync(() => t.CallAsync(tool.Name, """{"action":"ok"}"""));
                break;
            }
            case "cancel_generation":
            case "begin_turn":
            {
                var tool = new CallbackTool();
                await using var t = await TestEngine.CreateAsync(knownSecrets: secrets, extraTools: new SenseiTool[] { tool });
                t.Engine.BeginTurn("first");
                var pending = t.CallAsync(tool.Name, """{"action":"ok"}""");
                await tool.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    error = Record.Exception(() =>
                    {
                        if (entryPoint == "begin_turn") t.Engine.BeginTurn("second");
                        else t.Engine.CancelGeneration();
                    });
                }
                finally { tool.Release.TrySetResult(true); }
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                break;
            }
            default:
            {
                CallbackRunner? runner = null;
                await using var t = await TestEngine.CreateCoreAsync(new ScriptedChatClient(), null, null, null, null, null, "default", secrets, null, null,
                    core => runner = new CallbackRunner(core));
                var running = t.Engine.RunTurnAsync("first");
                await runner!.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    error = entryPoint switch
                    {
                        "next_turn_cancels_running_turn" => await Record.ExceptionAsync(() => t.Engine.RunTurnAsync("second")),
                        "reset_cancels_running_turn" => await Record.ExceptionAsync(() => t.Engine.ResetProfileAsync()),
                        _ => await Record.ExceptionAsync(async () => await t.Engine.DisposeAsync()),
                    };
                }
                finally { runner.Release.TrySetResult(true); }
                await running.WaitAsync(TimeSpan.FromSeconds(5));
                break;
            }
        }

        var safe = Assert.IsType<SenseiEngineException>(error);
        AssertBoundaryException(safe);
    }

    private sealed class CallbackTool : ProbeTool
    {
        public CallbackTool() : base("test_callback")
        {
        }

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
        {
            using var registration = ct.Register(() => throw new IOException("cancel callback: " + Secret));
            Started.TrySetResult(true);
            await Release.Task;
            return context.Locked(() => context.Success());
        }
    }

    [Fact]
    public void S77_every_public_member_of_the_engine_is_declared_through_the_boundary()
    {
        var source = File.ReadAllText(Path.Combine(LibraryDirectory(), "SenseiEngine.cs"));
        var members = Regex.Split(source, @"\r?\n(?=    (?:///|public |internal |private ))")
            .Where(m => Regex.IsMatch(m, @"^\s*public [^\r\n=]*\(", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace) || m.Contains("\n    public ", StringComparison.Ordinal))
            .Select(m => m.Substring(m.IndexOf("public ", StringComparison.Ordinal) < 0 ? 0 : m.IndexOf("public ", StringComparison.Ordinal)))
            .Where(m => m.StartsWith("public ", StringComparison.Ordinal))
            .ToList();

        var methods = members.Where(m => Regex.IsMatch(m.Split('\n')[0], @"^public [^=({]*\w+(<[^>]*>)?\(")).ToList();
        Assert.True(methods.Count >= 16, "expected the public methods of SenseiEngine, found " + methods.Count);
        foreach (var method in methods)
        {
            var declaration = method.Split('\n')[0];
            if (declaration.Contains("static", StringComparison.Ordinal) && declaration.Contains("CreateAsync", StringComparison.Ordinal))
            {
                // CreateAsync(options, ct) delegates to the internal overload, which owns the boundary.
                Assert.Contains("CreateAsync(options, null, null, ct)", method);
                continue;
            }
            var body = method.Split("\n    ///", 2)[0].Split("\n    internal ", 2)[0].Split("\n    private ", 2)[0];
            Assert.True(body.Contains("Guard(", StringComparison.Ordinal) || body.Contains("GuardAsync(", StringComparison.Ordinal),
                "S77: public member does not go through the exception boundary: " + declaration.Trim());
        }

        var create = source.Substring(source.IndexOf("internal static async Task<SenseiEngine> CreateAsync", StringComparison.Ordinal));
        Assert.Contains("ExceptionSanitizer.ForHost", create.Split("\n    internal EngineCore Core", 2)[0]);
    }

    private static string LibraryDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var viaRoot = Path.Combine(dir.FullName, "sensei-engine", "src", "Sensei.Engine");
            if (File.Exists(Path.Combine(viaRoot, "Sensei.Engine.csproj"))) return viaRoot;
        }
        throw new DirectoryNotFoundException("sensei-engine/src/Sensei.Engine not found above " + AppContext.BaseDirectory);
    }

    // ------------------------------------------------------------------ documented exceptions keep their type, lose their inner

    [Fact]
    public async Task S77_argument_exceptions_keep_type_and_parameter_name_and_say_the_parameter_once()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });

        var nullPatch = await Record.ExceptionAsync(() => t.Engine.SetPreferenceByPlayerAsync(null!));
        var badBand = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("not-a-band"));

        var nullError = Assert.IsType<ArgumentNullException>(nullPatch);
        Assert.Equal("patch", nullError.ParamName);
        Assert.Single(Regex.Matches(nullError.Message, @"\(Parameter 'patch'\)"));
        var bandError = Assert.IsType<ArgumentException>(badBand);
        Assert.Equal("band", bandError.ParamName);
        Assert.Single(Regex.Matches(bandError.Message, @"\(Parameter 'band'\)"));
        Assert.Contains("starting band must be one of", bandError.Message);
        Assert.Null(bandError.InnerException);
    }

    [Fact]
    public async Task S77_a_bad_profile_id_is_an_ArgumentException_from_create()
    {
        var options = new SenseiEngineOptions
        {
            ChatClient = null,
            Speech = new RecordingSpeechOutput(),
            ProfileStore = new InMemoryProfileStore(),
            ProfileId = "NOT A VALID ID",
            KnownSecrets = new[] { Secret },
        };

        var error = await Record.ExceptionAsync(() => SenseiEngine.CreateAsync(options));

        var invalid = Assert.IsType<ArgumentException>(error);
        Assert.Null(invalid.InnerException);
        Assert.NotNull(invalid.ParamName);
    }

    [Fact]
    public async Task S77_a_null_options_argument_is_an_ArgumentNullException_from_create()
    {
        var error = await Record.ExceptionAsync(() => SenseiEngine.CreateAsync(null!));

        var nullOptions = Assert.IsType<ArgumentNullException>(error);
        Assert.Equal("options", nullOptions.ParamName);
    }

    [Fact]
    public async Task S77_turn_in_progress_is_an_InvalidOperationException_without_inner()
    {
        CallbackRunner? runner = null;
        await using var t = await TestEngine.CreateCoreAsync(new ScriptedChatClient(), null, null, null, null, null, "default", null, null, null,
            core => runner = new CallbackRunner(core));
        var running = t.Engine.RunTurnAsync("first");
        await runner!.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var error = Record.Exception(() => t.Engine.BeginTurn("second"));

            var busy = Assert.IsType<InvalidOperationException>(error);
            Assert.Contains("turn_in_progress", busy.Message);
            Assert.Null(busy.InnerException);
        }
        finally { runner.Release.TrySetResult(true); }
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task S77_a_dependency_InvalidOperationException_keeps_its_type_but_not_its_inner_or_secret()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Store.FailSavesWith = new InvalidOperationException("store state: " + Secret, new LeakyException(null));

        var error = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));

        Assert.IsType<InvalidOperationException>(error);
        AssertBoundaryException(error);
        Assert.Contains("[redacted]", error!.Message);
    }

    [Fact]
    public async Task S77_a_subclass_of_a_documented_exception_is_not_passed_through()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Store.FailSavesWith = new DerivedInvalidOperation("derived: " + Secret);

        var error = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));

        var safe = Assert.IsType<SenseiEngineException>(error);
        Assert.Equal(typeof(DerivedInvalidOperation).FullName, safe.OriginalType);
        AssertBoundaryException(safe);
    }

    [Fact]
    public async Task S77_a_corrupt_profile_stays_a_ProfileCorruptException_without_its_inner()
    {
        var store = new InMemoryProfileStore();
        var failing = new CorruptOnLoadStore(store, new ProfileCorruptException("default", "/data/default.json", new FormatException("bad " + Secret)));
        var options = new SenseiEngineOptions
        {
            ChatClient = null,
            Speech = new RecordingSpeechOutput(),
            ProfileStore = failing,
            KnownSecrets = new[] { Secret },
        };

        var error = await Record.ExceptionAsync(() => SenseiEngine.CreateAsync(options));

        var corrupt = Assert.IsType<ProfileCorruptException>(error);
        Assert.Equal("default", corrupt.ProfileId);
        Assert.Null(corrupt.InnerException);
        Assert.DoesNotContain(Secret, corrupt.ToString());
    }

    private sealed class CorruptOnLoadStore : IProfileStore
    {
        private readonly InMemoryProfileStore _inner;
        private readonly Exception _failure;

        public CorruptOnLoadStore(InMemoryProfileStore inner, Exception failure)
        {
            _inner = inner;
            _failure = failure;
        }

        public Task<LearnerProfile?> LoadAsync(string profileId, CancellationToken ct) => throw _failure;

        public Task SaveAsync(LearnerProfile profile, CancellationToken ct) => _inner.SaveAsync(profile, ct);

        public Task DeleteAsync(string profileId, CancellationToken ct) => _inner.DeleteAsync(profileId, ct);
    }

    [Fact]
    public async Task S77_a_cancelled_caller_token_surfaces_as_an_OperationCanceledException_with_that_token()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var error = await Record.ExceptionAsync(() => t.Engine.ResetProfileAsync(cts.Token));

        var cancelled = Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(cts.Token, cancelled.CancellationToken);
        Assert.Null(cancelled.InnerException);
    }

    // ------------------------------------------------------------------ the boundary cannot be defeated by the exception itself

    [Fact]
    public async Task S77_an_exception_whose_members_throw_still_leaves_as_a_sanitized_SenseiEngineException()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Store.FailSavesWith = new HostileMessageException();

        var error = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));

        var safe = Assert.IsType<SenseiEngineException>(error);
        AssertBoundaryException(safe);
    }

    // ------------------------------------------------------------------ the trace

    [Theory]
    [InlineData(18, false)]
    [InlineData(18, true)]
    [InlineData(400, false)]
    public async Task S77_the_trace_holds_the_whole_sanitized_chain_not_a_truncated_one(int wrappers, bool secretAtRoot)
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        Exception failure = new IOException("leaf: " + Secret);
        for (var i = 0; i < wrappers; i++) failure = new IOException("wrapper " + i, failure);
        if (secretAtRoot) failure = new IOException("outer: " + Secret, failure);
        t.Store.FailSavesWith = failure;

        var error = await Record.ExceptionAsync(() => t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = false }));

        AssertBoundaryException(error);
        var entry = Assert.Single(t.Trace.Entries, e => e.Tool == "engine:SetPreferenceByPlayerAsync");
        Assert.Equal(ToolStatuses.Failure, entry.Status);
        Assert.Equal(ToolErrorCodes.InternalError, entry.ErrorCode);
        Assert.DoesNotContain(Secret, entry.Arguments);
        Assert.Contains("leaf: [redacted]", entry.Arguments);
        var last = wrappers + (secretAtRoot ? 1 : 0);
        Assert.Contains("[" + last + "]", entry.Arguments);
    }

    [Fact]
    public async Task S77_the_trace_of_a_failed_create_goes_to_the_hosts_sink()
    {
        var trace = new RecordingTraceSink();
        var store = new InMemoryProfileStore { FailSavesWith = new LeakyException(new IOException("socket " + Secret)) };

        var error = await Record.ExceptionAsync(() => TestEngine.CreateAsync(store: store, trace: trace, knownSecrets: new[] { Secret }));

        AssertBoundaryException(error);
        var entry = Assert.Single(trace.Entries, e => e.Tool == "engine:CreateAsync");
        Assert.DoesNotContain(Secret, entry.Arguments);
        Assert.Contains("[1]", entry.Arguments);
    }

    [Fact]
    public async Task S77_a_documented_exception_without_inner_writes_no_failure_trace()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });

        await Assert.ThrowsAsync<ArgumentException>(() => t.Engine.SetStartingBandAsync("not-a-band"));

        Assert.DoesNotContain(t.Trace.Entries, e => e.Tool.StartsWith("engine:", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the sanitizer's description

    [Fact]
    public void S77_describe_walks_a_very_deep_chain_without_a_depth_limit_and_without_recursion()
    {
        var secrets = new Secrets(new[] { Secret });
        Exception chain = new IOException("leaf " + Secret);
        for (var i = 0; i < 20_000; i++) chain = new IOException("wrapper " + i, chain);

        var text = ExceptionSanitizer.Describe(chain, secrets);

        Assert.DoesNotContain(Secret, text);
        Assert.Contains("[20000]", text);
        Assert.Contains("leaf [redacted]", text);
    }

    [Fact]
    public void S77_describe_covers_aggregate_members_data_source_help_link_and_stack()
    {
        var secrets = new Secrets(new[] { Secret });
        Exception thrown;
        try
        {
            throw new LeakyException(null);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        var aggregate = new AggregateException("many", thrown, new IOException("second " + Secret, new LeakyException(null)));

        var text = ExceptionSanitizer.Describe(aggregate, secrets);

        Assert.DoesNotContain(Secret, text);
        Assert.Contains("[redacted]", text);
        Assert.Contains("data credential = [redacted]", text);
        Assert.Contains("source: driver-[redacted]", text);
        Assert.Contains("   at Sensei.Engine.Tests.Foundation.ExceptionBoundaryTests.", text);
        Assert.Contains("second [redacted]", text);
    }

    [Fact]
    public void S77_describe_describes_a_shared_exception_once_and_names_the_back_reference()
    {
        var secrets = new Secrets(new[] { Secret });
        var shared = new IOException("shared " + Secret, new LeakyException(null));
        var holder = new AggregateException("diamond", new InvalidOperationException("left", shared), new TimeoutException("right", shared));

        var text = ExceptionSanitizer.Describe(holder, secrets);

        Assert.DoesNotContain(Secret, text);
        Assert.Single(Regex.Matches(text, "shared \\[redacted\\]"));
        Assert.Contains("refers back to", text);
    }

    [Fact]
    public void S77_for_host_survives_an_exception_whose_message_throws()
    {
        var secrets = new Secrets(new[] { Secret });

        var safe = ExceptionSanitizer.ForHost(new HostileMessageException(), secrets, out var detail);

        var engine = Assert.IsType<SenseiEngineException>(safe);
        Assert.Null(engine.InnerException);
        Assert.DoesNotContain(Secret, engine.ToString());
        Assert.Null(detail);
    }

    [Fact]
    public void S77_for_host_keeps_data_out_of_the_replacement()
    {
        var secrets = new Secrets(new[] { Secret });
        var ex = new LeakyException(null);

        var safe = ExceptionSanitizer.ForHost(ex, secrets, out _);

        Assert.Empty(safe.Data);
        Assert.Null(safe.HelpLink);
        Assert.NotEqual(ex.Source, safe.Source);
        foreach (DictionaryEntry entry in safe.Data) Assert.DoesNotContain(Secret, entry.Value?.ToString() ?? "");
    }
}

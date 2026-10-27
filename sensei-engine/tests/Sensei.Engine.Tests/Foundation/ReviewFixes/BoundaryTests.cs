using System.Text.Json;
using System.Text.Json.Nodes;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Storage;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>
/// Companions of the review round 1 tests: the same causes reached through the other engine boundaries and shapes
/// (S77 exceptions, S78 spans, S57 nested members, S27 start-up cancellation).
/// </summary>
public sealed class BoundaryTests
{
    private const string Secret = "fictional-store-credential";

    private sealed class DependencyFailure : Exception
    {
        public DependencyFailure(string message) : base(message)
        {
        }
    }

    private sealed class ThrowingTool : ProbeTool
    {
        public ThrowingTool() : base("test_throw_secret")
        {
        }

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
        {
            Started.TrySetResult(true);
            await Release.Task;
            throw new InvalidOperationException("tool dependency failed: " + Secret);
        }
    }

    // ------------------------------------------------------------------ S77: exceptions leave the engine redacted

    [Fact]
    public async Task S77_a_secret_in_an_inner_exception_is_redacted_and_the_outer_type_is_kept()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Store.FailSavesWith = new IOException("store write failed", new InvalidOperationException("inner " + Secret));

        var error = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("early_beginner"));

        Assert.IsType<IOException>(error);
        Assert.DoesNotContain(Secret, error!.ToString());
        Assert.Contains("[redacted]", error.InnerException!.Message);
    }

    [Fact]
    public async Task S77_an_exception_of_another_type_is_replaced_by_one_that_names_its_type()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Store.FailSavesWith = new DependencyFailure("custom failure " + Secret);

        var error = await Record.ExceptionAsync(() => t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = false }));

        var safe = Assert.IsType<SanitizedException>(error);
        Assert.Contains(nameof(DependencyFailure), safe.OriginalType);
        Assert.DoesNotContain(Secret, safe.ToString());
        Assert.DoesNotContain(Secret, string.Join('|', safe.Data.Values.Cast<object?>()));
    }

    [Fact]
    public async Task S77_an_exception_with_nothing_to_redact_is_rethrown_as_it_was()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        var original = new IOException("disk full");
        t.Store.FailSavesWith = original;

        var error = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("advanced"));

        Assert.Same(original, error);
    }

    [Fact]
    public async Task S77_a_store_failure_while_creating_the_engine_is_redacted()
    {
        var store = new InMemoryProfileStore { FailSavesWith = new IOException("create failed: " + Secret) };

        var error = await Record.ExceptionAsync(() => TestEngine.CreateAsync(store: store, knownSecrets: new[] { Secret }));

        Assert.NotNull(error);
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.Contains("[redacted]", error.Message);
    }

    [Fact]
    public async Task S77_a_store_failure_during_reset_is_redacted()
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret });
        t.Store.FailSavesWith = new IOException("reset failed: " + Secret);

        var error = await Record.ExceptionAsync(() => t.Engine.ResetProfileAsync());

        Assert.NotNull(error);
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.Contains("[redacted]", error.Message);
    }

    [Fact]
    public async Task S77_a_tool_body_exception_is_redacted_for_the_caller_and_for_a_waiting_duplicate()
    {
        var tool = new ThrowingTool();
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { Secret }, extraTools: new SenseiTool[] { tool });
        t.Engine.BeginTurn("x");
        var first = t.CallAsync(tool.Name, """{"action":"ok"}""", "same");
        await tool.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = t.CallAsync(tool.Name, """{"action":"ok"}""", "same");
        tool.Release.TrySetResult(true);

        var firstError = await Record.ExceptionAsync(() => first);
        var duplicateError = await Record.ExceptionAsync(() => duplicate);

        Assert.IsType<InvalidOperationException>(firstError);
        Assert.IsType<InvalidOperationException>(duplicateError);
        Assert.DoesNotContain(Secret, firstError!.ToString());
        Assert.DoesNotContain(Secret, duplicateError!.ToString());
        Assert.Contains("[redacted]", firstError.Message);
    }

    // ------------------------------------------------------------------ S78: spans are matched against the original text

    [Fact]
    public void S78_a_known_secret_inside_a_long_run_leaves_no_fragment_of_the_run()
    {
        var secrets = new Secrets(new[] { "HUNTERVALUE9" });
        var run = "aaaaaaaaaaaaaaaa1" + "HUNTERVALUE9" + "bbbbbbbbbbbbbbbb";

        Assert.Equal("[redacted]", secrets.Sanitize(run));
        Assert.Equal("see [redacted] now", secrets.Sanitize("see " + run + " now"));
    }

    [Fact]
    public void S78_touching_and_overlapping_matches_become_one_redaction()
    {
        Assert.Equal("[redacted]", CredentialPattern.Redact("token=abcdefgh" + "sk-abcdefghijklmnopq"));
        Assert.Equal("a [redacted] b", CredentialPattern.Redact("a sk-abcdefghijklmnopq b"));
    }

    // ------------------------------------------------------------------ S57: every persisted member must be present

    public static TheoryData<string> RequiredMembers => new()
    {
        "version", "profile_id", "created_at", "updated_at", "working_level", "level_history", "support", "pinned", "evidence", "notes", "sessions",
        "support/show_japanese", "support/pace", "support/language_mode",
        "level_history/0/from", "level_history/0/reason", "level_history/0/at",
        "sessions/0/session_id", "sessions/0/started_at", "sessions/0/preference_changes",
        "sessions/0/preference_changes/0/field", "sessions/0/preference_changes/0/by",
        "evidence/0/attempt_id", "evidence/0/outcome", "evidence/0/event_ids", "evidence/0/actors", "evidence/0/target",
        "evidence/0/target/key", "evidence/0/target/dimension", "evidence/0/at",
    };

    private static JsonNode FullProfileJson()
    {
        var now = new DateTimeOffset(2026, 10, 27, 9, 30, 15, TimeSpan.Zero);
        var profile = LearnerProfile.CreateDefault("default", now);
        profile.LevelHistory.Add(new LevelChange { From = "pre_n5", To = "n5", Reason = "band", At = now });
        var session = new SessionRecord { SessionId = "s-20261027093015-0a0b", StartedAt = now };
        session.PreferenceChanges.Add(new PreferenceChange { Field = "pace", From = "normal", To = "slow", By = "player" });
        profile.Sessions.Add(session);
        profile.Evidence.Add(new EvidenceRecord
        {
            AttemptId = "a1", SessionId = session.SessionId, TurnId = 1, Target = new SkillTarget { Dimension = "vocabulary", Key = "ringo" },
            EngineOutcome = EngineOutcomes.NoTask, LanguageAssessment = LanguageAssessments.Correct, CommunicativeSuccess = true,
            Outcome = AttemptOutcomes.IndependentSuccess, At = now,
        });
        return JsonNode.Parse(JsonSerializer.Serialize(profile, SenseiJsonContext.Default.LearnerProfile))!;
    }

    private static void Remove(JsonNode root, string path)
    {
        var parts = path.Split('/');
        var node = root;
        for (var i = 0; i < parts.Length - 1; i++)
            node = int.TryParse(parts[i], out var index) ? node.AsArray()[index]! : node[parts[i]]!;
        node.AsObject().Remove(parts[^1]);
    }

    private static async Task<(string Directory, FileProfileStore Store)> StoreWithAsync(JsonNode json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sensei-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new FileProfileStore(directory);
        await File.WriteAllTextAsync(store.PathFor("default"), json.ToJsonString());
        return (directory, store);
    }

    [Fact]
    public async Task S57_a_complete_profile_file_loads()
    {
        var (directory, store) = await StoreWithAsync(FullProfileJson());
        try
        {
            var profile = await store.LoadAsync("default", CancellationToken.None);

            Assert.NotNull(profile);
            Assert.Equal("ringo", profile.Evidence.Single().Target.Key);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [MemberData(nameof(RequiredMembers))]
    public async Task S57_a_profile_file_missing_a_required_member_is_corrupt_and_left_untouched(string path)
    {
        var json = FullProfileJson();
        Remove(json, path);
        var (directory, store) = await StoreWithAsync(json);
        try
        {
            var file = store.PathFor("default");
            var before = await File.ReadAllTextAsync(file);

            await Assert.ThrowsAsync<ProfileCorruptException>(() => store.LoadAsync("default", CancellationToken.None));

            Assert.Equal(before, await File.ReadAllTextAsync(file));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task S57_absent_nullable_members_mean_null()
    {
        var json = FullProfileJson();
        Remove(json, "starting_band");
        Remove(json, "self_reported_jlpt");
        Remove(json, "sessions/0/next_suggestion");
        Remove(json, "evidence/0/transcript_excerpt");
        var (directory, store) = await StoreWithAsync(json);
        try
        {
            var profile = await store.LoadAsync("default", CancellationToken.None);

            Assert.NotNull(profile);
            Assert.Null(profile.StartingBand);
            Assert.Null(profile.Evidence.Single().TranscriptExcerpt);
        }
        finally { Directory.Delete(directory, true); }
    }

    // ------------------------------------------------------------------ S27: cancellation before the first turn

    [Fact]
    public async Task S27_cancelled_before_the_first_turn_stays_cancelled_until_the_next_begin_turn()
    {
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { new ProbeTool() });
        t.Engine.CancelGeneration();

        Assert.Equal(ToolStatuses.Cancelled, (await t.CallAsync("test_probe", """{"action":"ok"}""")).Status);
        Assert.Equal(ToolStatuses.Cancelled, (await t.CallAsync("test_probe", """{"action":"ok"}""")).Status);

        t.Engine.BeginTurn("x");
        Assert.Equal(ToolStatuses.Success, (await t.CallAsync("test_probe", """{"action":"ok"}""")).Status);
    }

    [Fact]
    public async Task S27_without_any_cancellation_a_call_before_the_first_turn_is_still_no_turn()
    {
        await using var t = await TestEngine.CreateAsync();

        Assert.Equal(ToolErrorCodes.NoTurn, (await t.CallAsync("inspect_scene", "{}")).ErrorCode);
    }
}

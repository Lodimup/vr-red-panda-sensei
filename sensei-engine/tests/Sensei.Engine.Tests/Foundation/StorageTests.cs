using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Storage;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Foundation;

public sealed class StorageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 18, 30, 15, TimeSpan.FromHours(9));

    private static readonly string[] TopLevelFields =
    {
        "version", "profile_id", "created_at", "updated_at", "starting_band", "self_reported_jlpt", "working_level",
        "level_history", "support", "pinned", "evidence", "notes", "sessions",
    };

    private static readonly string[] SessionFields = { "session_id", "started_at", "preference_changes", "next_suggestion" };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sensei-storage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private FileProfileStore NewStore() => new(_dir);

    private static LearnerProfile RichProfile(string id)
    {
        var p = LearnerProfile.CreateDefault(id, Now);
        p.StartingBand = "early_beginner";
        p.SelfReportedJlpt = "n5";
        p.WorkingLevel = WorkingLevels.N5;
        p.UpdatedAt = Now.AddMinutes(5);
        p.LevelHistory.Add(new LevelChange
        {
            From = WorkingLevels.PreN5,
            To = WorkingLevels.N5,
            Reason = "band_selected",
            At = Now,
            EvidenceIds = new List<string> { "a1" },
        });
        p.Support.ShowRomaji = false;
        p.Support.Pace = "slow";
        p.Pinned.Add("show_romaji");
        p.Evidence.Add(new EvidenceRecord
        {
            AttemptId = "a1",
            SessionId = "s-20261027093015-ab12",
            TurnId = 3,
            Target = new SkillTarget { Dimension = "vocabulary", Key = "cup", Japanese = "コップ" },
            TranscriptExcerpt = "コップをください",
            EventIds = new List<long> { 4, 5 },
            Actors = new List<string> { "player" },
            EngineOutcome = EngineOutcomes.TaskCompleted,
            TaskId = "t1",
            LanguageAssessment = LanguageAssessments.Correct,
            CommunicativeSuccess = true,
            Assistance = new List<string> { "hint" },
            Outcome = AttemptOutcomes.SuccessAfterHint,
            At = Now,
        });
        p.Notes["favorite_food"] = "おにぎり";
        p.Notes["goal"] = "order tea at a cafe";
        p.Sessions.Add(new SessionRecord
        {
            SessionId = "s-20261027093015-ab12",
            StartedAt = Now,
            PreferenceChanges = new List<PreferenceChange>
            {
                new() { Field = "show_romaji", From = "true", To = "false", By = "player" },
            },
            NextSuggestion = "Try asking for the cup again.",
        });
        p.Sessions.Add(new SessionRecord { SessionId = "s-20261028090000-cd34", StartedAt = Now.AddDays(1) });
        return p;
    }

    private static string ToJson(LearnerProfile p) => JsonSerializer.Serialize(p, SenseiJsonContext.Default.LearnerProfile);

    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static IEnumerable<string> AllPropertyNames(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var kv in o)
                {
                    yield return kv.Key;
                    foreach (var inner in AllPropertyNames(kv.Value)) yield return inner;
                }
                break;
            case JsonArray a:
                foreach (var item in a)
                    foreach (var inner in AllPropertyNames(item)) yield return inner;
                break;
        }
    }

    // ------------------------------------------------------------------ S57: round trip and file layout

    [Fact]
    public async Task S57_round_trip_restores_evidence_notes_sessions_level_history_and_pinned()
    {
        var store = NewStore();
        var profile = RichProfile("alpha");

        await store.SaveAsync(profile, default);
        var loaded = await store.LoadAsync("alpha", default);

        Assert.NotNull(loaded);
        Assert.Equal(ToJson(profile), ToJson(loaded!));
        Assert.Equal(1, loaded!.Version);
        Assert.Equal("alpha", loaded.ProfileId);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Equal(Now.AddMinutes(5), loaded.UpdatedAt);
        Assert.Equal("early_beginner", loaded.StartingBand);
        Assert.Equal("n5", loaded.SelfReportedJlpt);
        Assert.Equal(WorkingLevels.N5, loaded.WorkingLevel);
        Assert.False(loaded.Support.ShowRomaji);
        Assert.Equal("slow", loaded.Support.Pace);
        Assert.Equal(new[] { "show_romaji" }, loaded.Pinned);

        var change = Assert.Single(loaded.LevelHistory);
        Assert.Equal("band_selected", change.Reason);
        Assert.Equal(new[] { "a1" }, change.EvidenceIds);

        var evidence = Assert.Single(loaded.Evidence);
        Assert.Equal("a1", evidence.AttemptId);
        Assert.Equal("cup", evidence.Target.Key);
        Assert.Equal("コップをください", evidence.TranscriptExcerpt);
        Assert.Equal(new long[] { 4, 5 }, evidence.EventIds);
        Assert.Equal(AttemptOutcomes.SuccessAfterHint, evidence.Outcome);
        Assert.True(evidence.CommunicativeSuccess);

        Assert.Equal("おにぎり", loaded.Notes["favorite_food"]);
        Assert.Equal("order tea at a cafe", loaded.Notes["goal"]);

        Assert.Equal(2, loaded.Sessions.Count);
        Assert.Equal("Try asking for the cup again.", loaded.Sessions[0].NextSuggestion);
        var pref = Assert.Single(loaded.Sessions[0].PreferenceChanges);
        Assert.Equal(("show_romaji", "true", "false", "player"), (pref.Field, pref.From, pref.To, pref.By));
        Assert.Null(loaded.Sessions[1].NextSuggestion);
    }

    [Fact]
    public async Task S57_round_trip_of_a_default_profile_keeps_null_band()
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault("default", Now), default);

        var loaded = await store.LoadAsync("default", default);

        Assert.NotNull(loaded);
        Assert.Null(loaded!.StartingBand);
        Assert.Null(loaded.SelfReportedJlpt);
        Assert.Equal(WorkingLevels.PreN5, loaded.WorkingLevel);
        Assert.Empty(loaded.Evidence);
        Assert.Empty(loaded.Notes);
        Assert.Empty(loaded.Sessions);
        Assert.Empty(loaded.Pinned);
    }

    [Fact]
    public async Task S57_file_is_named_after_the_profile_id_with_json_extension()
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault("alpha", Now), default);

        Assert.Equal("alpha.json", Path.GetFileName(store.PathFor("alpha")));
        Assert.Equal(_dir, Path.GetDirectoryName(store.PathFor("alpha")));
        Assert.Equal(new[] { Path.Combine(_dir, "alpha.json") }, Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task S57_save_creates_a_missing_directory()
    {
        var nested = Path.Combine(_dir, "a", "b");
        var store = new FileProfileStore(nested);
        Assert.False(Directory.Exists(nested));

        await store.SaveAsync(LearnerProfile.CreateDefault("alpha", Now), default);

        Assert.True(File.Exists(Path.Combine(nested, "alpha.json")));
    }

    [Fact]
    public async Task S57_save_overwrites_the_previous_profile()
    {
        var store = NewStore();
        var profile = LearnerProfile.CreateDefault("alpha", Now);
        await store.SaveAsync(profile, default);

        profile.StartingBand = "advanced";
        profile.Notes["goal"] = "read menus";
        await store.SaveAsync(profile, default);

        var loaded = await store.LoadAsync("alpha", default);
        Assert.Equal("advanced", loaded!.StartingBand);
        Assert.Equal("read menus", loaded.Notes["goal"]);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task S57_profiles_with_different_ids_are_stored_separately()
    {
        var store = NewStore();
        var a = LearnerProfile.CreateDefault("alpha", Now);
        a.StartingBand = "zero";
        var b = LearnerProfile.CreateDefault("beta", Now);
        b.StartingBand = "advanced";
        await store.SaveAsync(a, default);
        await store.SaveAsync(b, default);

        Assert.Equal("zero", (await store.LoadAsync("alpha", default))!.StartingBand);
        Assert.Equal("advanced", (await store.LoadAsync("beta", default))!.StartingBand);
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public async Task S57_no_temp_files_are_left_after_the_first_save()
    {
        var store = NewStore();
        await store.SaveAsync(RichProfile("alpha"), default);

        var files = Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "alpha.json" }, files);
    }

    [Fact]
    public async Task S57_no_temp_files_are_left_after_repeated_overwrites()
    {
        var store = NewStore();
        var profile = RichProfile("alpha");
        for (var i = 0; i < 5; i++)
        {
            profile.UpdatedAt = Now.AddMinutes(i);
            await store.SaveAsync(profile, default);
        }

        var files = Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "alpha.json" }, files);
        Assert.DoesNotContain(files, f => f!.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S57_concurrent_saves_of_one_profile_leave_one_valid_file_and_no_temp_files()
    {
        var store = NewStore();
        var tasks = Enumerable.Range(0, 8).Select(i =>
        {
            var p = RichProfile("alpha");
            p.Notes["n"] = i.ToString();
            return store.SaveAsync(p, default);
        });

        await Task.WhenAll(tasks);

        Assert.Equal(new[] { "alpha.json" }, Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).ToArray());
        Assert.NotNull(await store.LoadAsync("alpha", default));
    }

    [Fact]
    public async Task S57_missing_file_loads_as_null()
    {
        var store = NewStore();
        Assert.Null(await store.LoadAsync("alpha", default));

        Directory.CreateDirectory(_dir);
        Assert.Null(await store.LoadAsync("alpha", default));
    }

    [Fact]
    public async Task S57_loading_one_id_does_not_return_another_profiles_file()
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault("alpha", Now), default);
        Assert.Null(await store.LoadAsync("beta", default));
    }

    [Fact]
    public async Task S57_delete_removes_the_file()
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault("alpha", Now), default);
        await store.SaveAsync(LearnerProfile.CreateDefault("beta", Now), default);

        await store.DeleteAsync("alpha", default);

        Assert.False(File.Exists(store.PathFor("alpha")));
        Assert.Null(await store.LoadAsync("alpha", default));
        Assert.NotNull(await store.LoadAsync("beta", default));
    }

    [Fact]
    public async Task S57_deleting_a_missing_profile_does_not_throw()
    {
        var store = NewStore();
        await store.DeleteAsync("alpha", default);

        Directory.CreateDirectory(_dir);
        await store.DeleteAsync("alpha", default);
    }

    [Fact]
    public async Task S57_a_deleted_profile_can_be_saved_again()
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault("alpha", Now), default);
        await store.DeleteAsync("alpha", default);
        await store.SaveAsync(LearnerProfile.CreateDefault("alpha", Now), default);

        Assert.NotNull(await store.LoadAsync("alpha", default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void S57_constructor_rejects_a_blank_directory(string directory)
    {
        Assert.Throws<ArgumentException>(() => new FileProfileStore(directory));
    }

    // ------------------------------------------------------------------ S57: profile id rules

    public static TheoryData<string> InvalidIds() => new()
    {
        "Default",
        "ALPHA",
        "a b",
        new string('a', 41),
        "",
        "../x",
        "..",
        "a/b",
        "a\\b",
        "a.b",
        "alpha.json",
        "é",
        " alpha",
        "alpha ",
    };

    [Theory]
    [MemberData(nameof(InvalidIds))]
    public async Task S57_invalid_id_throws_ArgumentException_on_Load(string id)
    {
        var store = NewStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.LoadAsync(id, default));
    }

    [Theory]
    [MemberData(nameof(InvalidIds))]
    public async Task S57_invalid_id_throws_ArgumentException_on_Save_and_writes_nothing(string id)
    {
        var store = NewStore();
        var profile = LearnerProfile.CreateDefault(id, Now);

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.SaveAsync(profile, default));

        Assert.False(Directory.Exists(_dir) && Directory.GetFileSystemEntries(_dir).Length > 0);
    }

    [Theory]
    [MemberData(nameof(InvalidIds))]
    public async Task S57_invalid_id_throws_ArgumentException_on_Delete(string id)
    {
        var store = NewStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.DeleteAsync(id, default));
    }

    // `$` in `^[a-z0-9_-]{1,40}$` must anchor the true end of the id: a trailing newline is not in the allowed set.
    [Fact]
    public async Task S57_id_with_a_trailing_newline_is_invalid_on_Load()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await NewStore().LoadAsync("alpha\n", default));
    }

    [Fact]
    public async Task S57_id_with_a_trailing_newline_is_invalid_on_Save_and_writes_nothing()
    {
        var store = NewStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.SaveAsync(LearnerProfile.CreateDefault("alpha\n", Now), default));
        Assert.False(Directory.Exists(_dir) && Directory.GetFileSystemEntries(_dir).Length > 0);
    }

    [Fact]
    public async Task S57_id_with_a_trailing_newline_is_invalid_on_Delete()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await NewStore().DeleteAsync("alpha\n", default));
    }

    [Fact]
    public async Task S57_a_path_traversal_id_never_touches_files_outside_the_directory()
    {
        var parent = Path.GetDirectoryName(_dir)!;
        var outside = Path.Combine(parent, "x.json");
        var existed = File.Exists(outside);

        var store = NewStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.SaveAsync(LearnerProfile.CreateDefault("../x", Now), default));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.DeleteAsync("../x", default));

        Assert.Equal(existed, File.Exists(outside));
    }

    [Theory]
    [InlineData("a_b-9")]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("_")]
    [InlineData("-")]
    [InlineData("default")]
    public async Task S57_valid_ids_are_accepted_for_save_load_and_delete(string id)
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault(id, Now), default);
        Assert.Equal(id, (await store.LoadAsync(id, default))!.ProfileId);
        await store.DeleteAsync(id, default);
        Assert.Null(await store.LoadAsync(id, default));
    }

    [Fact]
    public async Task S57_an_id_of_exactly_40_chars_is_accepted()
    {
        var id = new string('a', 40);
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault(id, Now), default);

        Assert.Equal(id, (await store.LoadAsync(id, default))!.ProfileId);
        Assert.True(File.Exists(Path.Combine(_dir, id + ".json")));
    }

    [Fact]
    public async Task S57_an_id_of_41_chars_is_rejected()
    {
        var store = NewStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.LoadAsync(new string('a', 41), default));
    }

    [Fact]
    public async Task S57_null_profile_on_Save_throws()
    {
        var store = NewStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.SaveAsync(null!, default));
    }

    // ------------------------------------------------------------------ S57: corrupt files

    private static string CorruptBody(string kind, string validJson)
    {
        switch (kind)
        {
            case "garbage":
                return "this is not json at all \u0001\u0002";
            case "truncated":
                return validJson.Substring(0, validJson.Length / 2);
            case "empty":
                return "";
            case "json_null":
                return "null";
            case "json_array":
                return "[1,2,3]";
            case "wrong_profile_id":
            {
                var node = JsonNode.Parse(validJson)!.AsObject();
                node["profile_id"] = "someone_else";
                return node.ToJsonString();
            }
            case "version_2":
            {
                var node = JsonNode.Parse(validJson)!.AsObject();
                node["version"] = 2;
                return node.ToJsonString();
            }
            case "version_0":
            {
                var node = JsonNode.Parse(validJson)!.AsObject();
                node["version"] = 0;
                return node.ToJsonString();
            }
            case "wrong_field_type":
            {
                var node = JsonNode.Parse(validJson)!.AsObject();
                node["evidence"] = "not an array";
                return node.ToJsonString();
            }
            default:
                throw new ArgumentException(kind);
        }
    }

    public static TheoryData<string> CorruptKinds() => new()
    {
        "garbage", "truncated", "empty", "json_null", "json_array", "wrong_profile_id", "version_2", "version_0", "wrong_field_type",
    };

    [Theory]
    [MemberData(nameof(CorruptKinds))]
    public async Task S57_corrupt_file_throws_ProfileCorruptException_and_is_left_unchanged(string kind)
    {
        var store = NewStore();
        var valid = ToJson(RichProfile("alpha"));
        Directory.CreateDirectory(_dir);
        var path = store.PathFor("alpha");
        var body = CorruptBody(kind, valid);
        File.WriteAllText(path, body, new UTF8Encoding(false));
        var before = File.ReadAllBytes(path);

        var ex = await Assert.ThrowsAsync<ProfileCorruptException>(async () => await store.LoadAsync("alpha", default));

        Assert.Equal("alpha", ex.ProfileId);
        Assert.Equal(path, ex.FilePath);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new[] { "alpha.json" }, Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public async Task S57_corrupt_file_load_is_repeatable_and_never_silently_replaced()
    {
        var store = NewStore();
        Directory.CreateDirectory(_dir);
        var path = store.PathFor("alpha");
        File.WriteAllText(path, "{ broken");

        await Assert.ThrowsAsync<ProfileCorruptException>(async () => await store.LoadAsync("alpha", default));
        await Assert.ThrowsAsync<ProfileCorruptException>(async () => await store.LoadAsync("alpha", default));

        Assert.Equal("{ broken", File.ReadAllText(path));
    }

    [Fact]
    public async Task S57_a_corrupt_profile_does_not_stop_other_profiles_loading()
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault("beta", Now), default);
        File.WriteAllText(store.PathFor("alpha"), "garbage");

        await Assert.ThrowsAsync<ProfileCorruptException>(async () => await store.LoadAsync("alpha", default));
        Assert.NotNull(await store.LoadAsync("beta", default));
    }

    [Fact]
    public async Task S57_corrupt_exception_message_is_engine_authored_and_holds_neither_the_profile_nor_the_directory()
    {
        var store = NewStore();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.PathFor("alpha"), "garbage");

        var ex = await Assert.ThrowsAsync<ProfileCorruptException>(async () => await store.LoadAsync("alpha", default));

        // Round-four ruling (F02): no caller-supplied text in the message; the id is on ProfileId.
        Assert.Equal(ProfileCorruptException.FixedMessage, ex.Message);
        Assert.Equal("alpha", ex.ProfileId);
        Assert.DoesNotContain(_dir, ex.Message);
    }

    [Fact]
    public async Task S57_a_valid_current_version_file_with_matching_id_loads()
    {
        var store = NewStore();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.PathFor("alpha"), ToJson(RichProfile("alpha")));

        Assert.NotNull(await store.LoadAsync("alpha", default));
    }

    // ------------------------------------------------------------------ S58: JSON shape

    [Fact]
    public async Task S58_saved_profile_has_exactly_the_spec_top_level_fields()
    {
        var store = NewStore();
        await store.SaveAsync(RichProfile("alpha"), default);

        var root = ReadObject(store.PathFor("alpha"));

        Assert.Equal(TopLevelFields.OrderBy(x => x, StringComparer.Ordinal), root.Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(TopLevelFields.Length, root.Count);
    }

    [Fact]
    public async Task S58_a_new_profile_has_exactly_the_spec_top_level_fields()
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault("alpha", Now), default);

        var root = ReadObject(store.PathFor("alpha"));

        Assert.Equal(TopLevelFields.OrderBy(x => x, StringComparer.Ordinal), root.Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task S58_starting_band_is_present_as_null_for_a_new_profile()
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault("alpha", Now), default);

        var root = ReadObject(store.PathFor("alpha"));

        Assert.True(root.ContainsKey("starting_band"));
        Assert.Null(root["starting_band"]);
        Assert.True(root.ContainsKey("self_reported_jlpt"));
        Assert.Null(root["self_reported_jlpt"]);
    }

    [Fact]
    public async Task S58_starting_band_holds_the_band_once_chosen()
    {
        var store = NewStore();
        await store.SaveAsync(RichProfile("alpha"), default);

        var root = ReadObject(store.PathFor("alpha"));

        Assert.Equal("early_beginner", (string?)root["starting_band"]);
        Assert.Equal("n5", (string?)root["self_reported_jlpt"]);
        Assert.Equal("n5", (string?)root["working_level"]);
    }

    [Fact]
    public async Task S58_version_is_1_and_profile_id_is_stored()
    {
        var store = NewStore();
        await store.SaveAsync(RichProfile("alpha"), default);

        var root = ReadObject(store.PathFor("alpha"));

        Assert.Equal(JsonValueKind.Number, root["version"]!.GetValueKind());
        Assert.Equal(1, (int)root["version"]!);
        Assert.Equal("alpha", (string?)root["profile_id"]);
    }

    [Fact]
    public async Task S58_collection_fields_have_the_expected_json_kinds()
    {
        var store = NewStore();
        await store.SaveAsync(RichProfile("alpha"), default);

        var root = ReadObject(store.PathFor("alpha"));

        Assert.IsType<JsonArray>(root["level_history"]);
        Assert.IsType<JsonArray>(root["pinned"]);
        Assert.IsType<JsonArray>(root["evidence"]);
        Assert.IsType<JsonArray>(root["sessions"]);
        Assert.IsType<JsonObject>(root["notes"]);
        Assert.IsType<JsonObject>(root["support"]);
    }

    [Fact]
    public async Task S58_empty_collections_are_written_not_omitted()
    {
        var store = NewStore();
        await store.SaveAsync(LearnerProfile.CreateDefault("alpha", Now), default);

        var root = ReadObject(store.PathFor("alpha"));

        Assert.Empty(root["level_history"]!.AsArray());
        Assert.Empty(root["pinned"]!.AsArray());
        Assert.Empty(root["evidence"]!.AsArray());
        Assert.Empty(root["sessions"]!.AsArray());
        Assert.Empty(root["notes"]!.AsObject());
    }

    [Fact]
    public async Task S58_support_uses_the_spec_snake_case_field_names()
    {
        var store = NewStore();
        await store.SaveAsync(RichProfile("alpha"), default);

        var support = ReadObject(store.PathFor("alpha"))["support"]!.AsObject();

        Assert.Equal(
            new[] { "correction_style", "language_mode", "pace", "show_japanese", "show_reading", "show_romaji", "show_translation" },
            support.Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal));
        Assert.False((bool)support["show_romaji"]!);
        Assert.Equal("slow", (string?)support["pace"]);
    }

    [Fact]
    public async Task S58_session_objects_have_exactly_the_spec_fields()
    {
        var store = NewStore();
        await store.SaveAsync(RichProfile("alpha"), default);

        var sessions = ReadObject(store.PathFor("alpha"))["sessions"]!.AsArray();

        Assert.Equal(2, sessions.Count);
        foreach (var session in sessions)
        {
            var obj = session!.AsObject();
            Assert.Equal(SessionFields.OrderBy(x => x, StringComparer.Ordinal), obj.Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task S58_session_next_suggestion_is_present_as_null_until_set_and_holds_its_text_after()
    {
        var store = NewStore();
        await store.SaveAsync(RichProfile("alpha"), default);

        var sessions = ReadObject(store.PathFor("alpha"))["sessions"]!.AsArray();

        Assert.Equal("Try asking for the cup again.", (string?)sessions[0]!["next_suggestion"]);
        Assert.True(sessions[1]!.AsObject().ContainsKey("next_suggestion"));
        Assert.Null(sessions[1]!["next_suggestion"]);
        Assert.Equal("s-20261027093015-ab12", (string?)sessions[0]!["session_id"]);
        Assert.NotNull(sessions[0]!["started_at"]);
        Assert.Single(sessions[0]!["preference_changes"]!.AsArray());
        Assert.Empty(sessions[1]!["preference_changes"]!.AsArray());
    }

    [Fact]
    public async Task S58_every_property_name_is_snake_case_and_none_names_a_key_or_audio()
    {
        var store = NewStore();
        await store.SaveAsync(RichProfile("alpha"), default);

        var names = AllPropertyNames(ReadObject(store.PathFor("alpha"))).Distinct().ToArray();

        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            Assert.Matches(new Regex("^[a-z][a-z0-9_]*$"), name);
            Assert.DoesNotContain("audio", name);
            Assert.DoesNotContain("api_key", name);
            Assert.DoesNotContain("password", name);
        }
    }

    [Fact]
    public async Task S58_engine_created_profile_through_the_file_store_has_the_spec_shape()
    {
        var store = NewStore();
        var options = new SenseiEngineOptions
        {
            ChatClient = null,
            Speech = new RecordingSpeechOutput(),
            ProfileStore = store,
            ProfileId = "alpha",
            Clock = new ManualClock(),
        };
        await using var engine = await SenseiEngine.CreateAsync(options);

        var root = ReadObject(store.PathFor("alpha"));

        Assert.Equal(TopLevelFields.OrderBy(x => x, StringComparer.Ordinal), root.Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Null(root["starting_band"]);
        var session = Assert.Single(root["sessions"]!.AsArray());
        Assert.Equal(engine.SessionId, (string?)session!["session_id"]);
        Assert.Equal(SessionFields.OrderBy(x => x, StringComparer.Ordinal), session.AsObject().Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------ S58 / S77: secrets never persisted

    [Fact]
    public async Task S58_engine_created_profile_has_the_spec_shape_in_the_in_memory_store()
    {
        await using var te = await TestEngine.CreateAsync();

        var root = JsonNode.Parse(te.Store.RawJson("default")!)!.AsObject();

        Assert.Equal(TopLevelFields.OrderBy(x => x, StringComparer.Ordinal), root.Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal));
        Assert.True(root.ContainsKey("starting_band"));
        Assert.Null(root["starting_band"]);
    }

    [Fact]
    public async Task S58_saved_profile_with_known_secrets_set_never_contains_the_secret_text()
    {
        const string secret = "zq-example-secret-0001";
        await using var te = await TestEngine.CreateAsync(knownSecrets: new[] { secret });

        Assert.DoesNotContain(secret, te.Store.RawJson("default"));

        te.Engine.BeginTurn("please remember " + secret);
        await te.Engine.InvokeToolAsync("remember_note", "{\"key\":\"my_secret\",\"value\":\"" + secret + "\"}");
        await te.Engine.InvokeToolAsync("remember_note", "{\"key\":\"" + "k" + "\",\"value\":\"safe " + secret + " text\"}");

        var json = te.Store.RawJson("default")!;
        Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(te.Store.Peek("default")!, SenseiJsonContext.Default.LearnerProfile));
    }

    [Fact]
    public async Task S58_profile_view_returned_by_the_engine_does_not_contain_the_known_secret()
    {
        const string secret = "zq-example-secret-0002";
        await using var te = await TestEngine.CreateAsync(knownSecrets: new[] { secret });

        te.Engine.BeginTurn("my secret is " + secret);

        var view = JsonSerializer.Serialize(te.Engine.GetProfile(), SenseiJsonContext.Default.LearnerProfileView);
        Assert.DoesNotContain(secret, view);
    }
}

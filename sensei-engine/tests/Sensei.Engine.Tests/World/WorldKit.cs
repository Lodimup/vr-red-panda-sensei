using System.Text;
using System.Text.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Tests.Foundation;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.World;

/// <summary>Helpers for the world-tool tests: seed a scene, start a manual turn, build tool arguments, read the engine state.</summary>
internal static class WorldKit
{
    public const string ListenTarget = """{"dimension":"listening","key":"ringo","japanese":"りんご"}""";
    public const string ProductionTarget = """{"dimension":"production","key":"onegai"}""";

    /// <summary>An engine with <paramref name="seed"/> placed directly, then a manual turn begun (so the seeded ids are visible, S84).</summary>
    public static async Task<TestEngine> StartAsync(params (string CatalogId, string Region)[] seed)
    {
        var t = await TestEngine.CreateAsync();
        if (seed.Length > 0) SceneHelpers.Spawn(t, seed);
        t.Engine.BeginTurn("practice");
        return t;
    }

    public static string Obj(string id, string region) => $$"""{"type":"in_region","object_id":"{{id}}","region":"{{region}}"}""";

    public static string Selected(string id) => $$"""{"type":"selected","object_id":"{{id}}"}""";

    public static string Predicates(params string[] predicates) => "[" + string.Join(",", predicates) + "]";

    /// <summary>Arguments of <c>start_challenge</c>; every part can be replaced.</summary>
    public static string StartJson(string predicates, string family = "listen_arrange", string instruction = """{"english":"Do it."}""",
        string? targets = null, string? extra = null)
    {
        var sb = new StringBuilder();
        sb.Append("{\"family\":\"").Append(family).Append("\",\"instruction\":").Append(instruction);
        sb.Append(",\"skill_targets\":").Append(targets ?? "[" + ListenTarget + "]");
        sb.Append(",\"predicates\":").Append(predicates);
        if (extra is not null) sb.Append(',').Append(extra);
        sb.Append('}');
        return sb.ToString();
    }

    public static async Task<ToolResult> StartTaskAsync(TestEngine t, string predicates, string? targets = null, string? extra = null)
    {
        var r = await t.CallAsync("start_challenge", StartJson(predicates, targets: targets, extra: extra));
        Assert.True(r.IsSuccess, r.Detail);
        return r;
    }

    public static string SpawnJson(params (string CatalogId, string? Region, int? Quantity)[] entries) =>
        "{\"objects\":[" + string.Join(",", entries.Select(e =>
            "{\"catalog_id\":\"" + e.CatalogId + "\"" +
            (e.Region is null ? "" : ",\"region\":\"" + e.Region + "\"") +
            (e.Quantity is null ? "" : ",\"quantity\":" + e.Quantity) + "}")) + "]}";

    public static string MoveJson(string id, string purpose, string? region = null, string? container = null)
    {
        var dest = region is not null ? $"\"region\":\"{region}\"" : container is not null ? $"\"container_id\":\"{container}\"" : "";
        return $"{{\"object_id\":\"{id}\",\"destination\":{{{dest}}},\"purpose\":\"{purpose}\"}}";
    }

    public static string Ids(params string[] ids) => "{\"object_ids\":[" + string.Join(",", ids.Select(i => "\"" + i + "\"")) + "]}";

    public static long Revision(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.World.Revision;
    }

    public static List<SceneEvent> SceneLog(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.World.Events.Select(e => e.Clone()).ToList();
    }

    public static List<AssistanceRecord> Aid(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.Assistance.Records.ToList();
    }

    public static List<ObjectInstance> Instances(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.World.Instances.Select(i => i.Clone()).ToList();
    }

    public static ObjectInstance Inst(TestEngine t, string id) => Instances(t).Single(i => i.Id == id);

    public static bool Exists(TestEngine t, string id) => Instances(t).Any(i => i.Id == id);

    public static Sensei.Engine.Tasks.TaskView? ActiveTask(TestEngine t) => t.Engine.GetActiveTask();

    public static JsonElement Data(ToolResult r) => r.Data ?? throw new Xunit.Sdk.XunitException("result has no data");

    /// <summary>Asserts a failure with <paramref name="code"/> (and optional detail text) and that nothing in the scene changed (S22).</summary>
    public static async Task<ToolResult> AssertRefusedAsync(TestEngine t, string tool, string json, string code, string? detailContains = null)
    {
        var revision = Revision(t);
        var events = SceneLog(t).Count;
        var aid = Aid(t).Count;
        var instances = Instances(t).Select(i => (i.Id, i.Region, i.Slot, i.ContainerId)).ToList();
        var r = await t.CallAsync(tool, json);
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(code, r.ErrorCode);
        if (detailContains is not null) Assert.Contains(detailContains, r.Detail);
        Assert.Equal(revision, Revision(t));
        Assert.Equal(events, SceneLog(t).Count);
        Assert.Equal(aid, Aid(t).Count);
        Assert.Equal(instances, Instances(t).Select(i => (i.Id, i.Region, i.Slot, i.ContainerId)).ToList());
        return r;
    }
}

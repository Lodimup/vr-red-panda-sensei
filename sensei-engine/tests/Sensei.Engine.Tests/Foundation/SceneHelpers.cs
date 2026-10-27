using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>Drives the scene directly as Sensei (the world tools themselves belong to the world-tools slice).</summary>
internal static class SceneHelpers
{
    public static List<string> Spawn(TestEngine t, params (string CatalogId, string Region)[] items)
    {
        List<string> ids;
        lock (t.Core.Sync)
        {
            var requests = items.Select(i => new SpawnRequest(i.CatalogId, i.Region)).ToList();
            ids = t.Core.World.CommitSpawn(requests, SceneActors.Sensei, t.Core.CurrentTurnId).InstanceIds.ToList();
        }
        t.Core.FlushNotifications();
        return ids;
    }

    public static string Spawn(TestEngine t, string catalogId, string region = SceneRegions.Center) => Spawn(t, (catalogId, region))[0];

    public static SceneEvent SenseiMove(TestEngine t, string id, string? region, string? containerId, string purpose = MovePurposes.Arrange)
    {
        SceneEvent e;
        lock (t.Core.Sync) e = t.Core.World.CommitMove(id, region, containerId, SceneActors.Sensei, purpose, t.Core.CurrentTurnId);
        t.Core.FlushNotifications();
        return e;
    }

    public static SceneEvent Remove(TestEngine t, params string[] ids)
    {
        SceneEvent e;
        lock (t.Core.Sync) e = t.Core.World.CommitRemove(ids, SceneActors.Sensei, t.Core.CurrentTurnId);
        t.Core.FlushNotifications();
        return e;
    }

    public static ObjectInstance Instance(TestEngine t, string id)
    {
        lock (t.Core.Sync) return t.Core.World.Find(id)!.Clone();
    }
}

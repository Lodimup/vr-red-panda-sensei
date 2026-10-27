using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sensei.Engine.Catalog;
using Sensei.Engine.Tools;

namespace Sensei.Engine.World;

/// <summary>One object to create in <see cref="SceneWorld.CommitSpawn"/>.</summary>
public sealed record SpawnRequest(string CatalogId, string Variant, string Region);

/// <summary>
/// The authoritative practice scene (SPEC §5): instances, slots, revision, selection and the event log.
/// Every member must be used under the engine lock (<see cref="ToolContext.Sync"/>); commits evaluate the active
/// task and queue <c>OnSceneChanged</c>/<c>OnTaskChanged</c> notifications that are raised after the lock is released.
/// </summary>
public sealed class SceneWorld
{
    /// <summary>S13: at most 16 instances, counting those in containers and hands.</summary>
    public const int Capacity = 16;

    private readonly EngineCore _core;
    private readonly List<ObjectInstance> _instances = new();
    private readonly List<SceneEvent> _events = new();
    private readonly Dictionary<string, int> _idCounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _slotCounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _spawnEventIds = new(StringComparer.Ordinal);
    private long _nextEventId = 1;

    internal SceneWorld(EngineCore core)
    {
        _core = core;
    }

    /// <summary>Starts at 0; +1 per committed mutating call or player move (S14).</summary>
    public long Revision { get; private set; }

    /// <summary>Id of the last event, 0 when none.</summary>
    public long LastEventId => _nextEventId - 1;

    public string? SelectedId { get; private set; }

    /// <summary>Live instances in creation order. Do not mutate.</summary>
    public IReadOnlyList<ObjectInstance> Instances => _instances;

    /// <summary>Every event of this engine instance, oldest first.</summary>
    public IReadOnlyList<SceneEvent> Events => _events;

    public int Count => _instances.Count;

    public ObjectInstance? Find(string? id) => id is null ? null : _instances.FirstOrDefault(i => i.Id == id);

    public bool Exists(string? id) => Find(id) is not null;

    public bool IsContainer(string? id) => Find(id) is { } i && ObjectCatalog.IsContainer(i.CatalogId);

    /// <summary>Instances whose <c>container_id</c> is <paramref name="containerId"/>.</summary>
    public IEnumerable<ObjectInstance> ContentsOf(string containerId) => _instances.Where(i => i.ContainerId == containerId);

    public SceneEvent? FindEvent(long eventId) => eventId >= 1 && eventId <= _events.Count ? _events[(int)eventId - 1] : null;

    public IEnumerable<SceneEvent> EventsAfter(long eventId) => _events.Where(e => e.EventId > eventId);

    /// <summary>Event id of the <c>spawned</c> event that created <paramref name="id"/> (ids are never reused), or null if never created.</summary>
    public long? SpawnEventIdOf(string id) => _spawnEventIds.TryGetValue(id, out var e) ? e : null;

    /// <summary>The id the next instance of <paramref name="catalogId"/> will get, <paramref name="offset"/> ahead (S12).</summary>
    public string PeekInstanceId(string catalogId, int offset = 0)
    {
        _idCounters.TryGetValue(catalogId, out var n);
        return catalogId + "_" + (n + 1 + offset).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Destination rules shared by <c>move_object</c> and <see cref="SenseiEngine.PlayerMove"/> (S19, S31a, S31b).
    /// Null when the move is allowed.
    /// </summary>
    public ToolFailure? ValidateDestination(string objectId, string? region, string? containerId)
    {
        if ((region is null) == (containerId is null))
            return new ToolFailure(ToolErrorCodes.InvalidArguments, "destination: exactly one of region or container_id is required");
        if (region is not null && !SceneRegions.IsRegion(region))
            return new ToolFailure(ToolErrorCodes.InvalidArguments, $"destination.region: must be one of {string.Join(", ", SceneRegions.All)}");
        var obj = Find(objectId);
        if (obj is null) return new ToolFailure(ToolErrorCodes.UnknownObject, $"object_id: unknown object '{objectId}'");
        if (containerId is not null)
        {
            var container = Find(containerId);
            if (container is null) return new ToolFailure(ToolErrorCodes.UnknownObject, $"destination.container_id: unknown object '{containerId}'");
            if (!ObjectCatalog.IsContainer(container.CatalogId))
                return new ToolFailure(ToolErrorCodes.InvalidDestination, $"destination.container_id: '{containerId}' is not a container");
            if (containerId == objectId)
                return new ToolFailure(ToolErrorCodes.InvalidDestination, "destination: an object cannot move into itself");
            if (ObjectCatalog.IsContainer(obj.CatalogId))
                return new ToolFailure(ToolErrorCodes.InvalidDestination, "destination: a container cannot go into a container");
            if (obj.ContainerId == containerId)
                return new ToolFailure(ToolErrorCodes.InvalidDestination, $"destination: '{objectId}' is already in '{containerId}'");
        }
        else if (obj.ContainerId is null && obj.Region == region)
        {
            return new ToolFailure(ToolErrorCodes.InvalidDestination, $"destination: '{objectId}' is already in {region}");
        }
        return null;
    }

    /// <summary>Creates instances in order as one <c>spawned</c> event, revision +1 (S12, S14, S30c). Validate capacity first.</summary>
    public SceneEvent CommitSpawn(IReadOnlyList<SpawnRequest> requests, string actor, long turnId)
    {
        if (requests.Count == 0) throw new ArgumentException("nothing to spawn", nameof(requests));
        if (_instances.Count + requests.Count > Capacity) throw new InvalidOperationException("capacity_exceeded: validate before committing");
        var ids = new List<string>();
        foreach (var r in requests)
        {
            _idCounters.TryGetValue(r.CatalogId, out var n);
            n++;
            _idCounters[r.CatalogId] = n;
            var inst = new ObjectInstance
            {
                Id = r.CatalogId + "_" + n.ToString(CultureInfo.InvariantCulture),
                CatalogId = r.CatalogId,
                Variant = r.Variant,
                Region = r.Region,
                Slot = SceneRegions.IsPracticeArea(r.Region) ? NextSlot(r.Region) : 0,
            };
            _instances.Add(inst);
            ids.Add(inst.Id);
        }
        var regions = requests.Select(r => r.Region).Distinct().ToList();
        var e = Append(actor, SceneEventKinds.Spawned, ids, null, regions.Count == 1 ? regions[0] : null, null, turnId, mutates: true);
        foreach (var id in ids) _spawnEventIds[id] = e.EventId;
        _core.AfterCommit(e);
        return e;
    }

    /// <summary>Moves an object (and a container's contents) as one <c>moved</c> event, revision +1 (S15, S16, S31c). Validate first.</summary>
    public SceneEvent CommitMove(string objectId, string? region, string? containerId, string actor, string? purpose, long turnId)
    {
        var obj = Find(objectId) ?? throw new InvalidOperationException($"unknown object '{objectId}'");
        var from = obj.ContainerId ?? obj.Region;
        var affected = new List<string> { obj.Id };
        if (containerId is not null)
        {
            var container = Find(containerId)!;
            obj.ContainerId = container.Id;
            obj.Region = container.Region;
        }
        else
        {
            obj.ContainerId = null;
            obj.Region = region!;
            if (SceneRegions.IsPracticeArea(region)) obj.Slot = NextSlot(region!);
            foreach (var inner in ContentsOf(obj.Id))
            {
                inner.Region = obj.Region;
                affected.Add(inner.Id);
            }
        }
        var e = Append(actor, SceneEventKinds.Moved, affected, from, containerId ?? region, actor == SceneActors.Sensei ? purpose : null, turnId, mutates: true);
        _core.AfterCommit(e);
        return e;
    }

    /// <summary>Removes instances as one <c>removed</c> event, revision +1; a removed selection becomes null (S32b). Validate first.</summary>
    public SceneEvent CommitRemove(IReadOnlyList<string> ids, string actor, long turnId)
    {
        foreach (var id in ids)
        {
            var obj = Find(id) ?? throw new InvalidOperationException($"unknown object '{id}'");
            _instances.Remove(obj);
            if (SelectedId == id) SelectedId = null;
        }
        var e = Append(actor, SceneEventKinds.Removed, ids.ToList(), null, null, null, turnId, mutates: true);
        _core.AfterCommit(e);
        return e;
    }

    /// <summary>Sets the selection as a <c>selected</c> event, no revision change (S18, S105).</summary>
    public SceneEvent CommitSelect(string id, string actor, long turnId)
    {
        if (!Exists(id)) throw new InvalidOperationException($"unknown object '{id}'");
        SelectedId = id;
        var e = Append(actor, SceneEventKinds.Selected, new List<string> { id }, null, null, null, turnId, mutates: false);
        _core.AfterCommit(e);
        return e;
    }

    /// <summary>Appends a task lifecycle event, no revision change (S104). Used by the task book.</summary>
    internal SceneEvent AppendTaskEvent(string kind, string taskId, string actor, long turnId)
    {
        var e = Append(actor, kind, new List<string>(), null, null, null, turnId, mutates: false);
        e.TaskId = taskId;
        _core.QueueSceneChanged(e);
        return e;
    }

    /// <summary>S60/S87: removes every object as one <c>removed</c> event (revision +1) when anything exists; counters keep going.</summary>
    internal SceneEvent? ClearForReset(long turnId)
    {
        SelectedId = null;
        if (_instances.Count == 0) return null;
        var ids = _instances.Select(i => i.Id).ToList();
        _instances.Clear();
        var e = Append(SceneActors.Sensei, SceneEventKinds.Removed, ids, null, null, null, turnId, mutates: true);
        _core.QueueSceneChanged(e);
        return e;
    }

    /// <summary>A fresh snapshot (S20) including the active task and support settings.</summary>
    public SceneSnapshot Snapshot() => new()
    {
        Revision = Revision,
        SelectedId = SelectedId,
        Instances = _instances.Select(ToSnapshot).ToList(),
        ActiveTask = _core.Tasks.Active?.Clone(),
        Support = _core.Learner.Profile.Support.Clone(),
    };

    private static SnapshotInstance ToSnapshot(ObjectInstance i)
    {
        var entry = ObjectCatalog.Get(i.CatalogId);
        return new SnapshotInstance
        {
            Id = i.Id,
            CatalogId = i.CatalogId,
            Variant = i.Variant,
            Region = i.Region,
            Slot = i.Slot,
            ContainerId = i.ContainerId,
            Japanese = entry.Japanese,
            Reading = entry.Reading,
            English = entry.English,
        };
    }

    private int NextSlot(string region)
    {
        _slotCounters.TryGetValue(region, out var n);
        n++;
        _slotCounters[region] = n;
        return n;
    }

    private SceneEvent Append(string actor, string kind, List<string> ids, string? from, string? to, string? purpose, long turnId, bool mutates)
    {
        if (mutates) Revision++;
        var e = new SceneEvent
        {
            EventId = _nextEventId++,
            Revision = Revision,
            Actor = actor,
            Kind = kind,
            InstanceIds = ids,
            From = from,
            To = to,
            Purpose = purpose,
            TurnId = turnId,
            Sequence = _core.NextSequence(),
        };
        _events.Add(e);
        return e;
    }
}

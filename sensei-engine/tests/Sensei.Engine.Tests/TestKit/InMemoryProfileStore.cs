using System.Collections.Concurrent;
using System.Text.Json;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Storage;

namespace Sensei.Engine.Tests.TestKit;

/// <summary>An <see cref="IProfileStore"/> that keeps profiles as JSON in memory and counts calls.</summary>
public sealed class InMemoryProfileStore : IProfileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _profiles = new();
    private int _saves;
    private int _loads;
    private int _deletes;

    public int SaveCount => _saves;
    public int LoadCount => _loads;
    public int DeleteCount => _deletes;

    /// <summary>When set, <see cref="SaveAsync"/> throws it.</summary>
    public Exception? FailSavesWith { get; set; }

    public bool Contains(string profileId) => _profiles.ContainsKey(profileId);

    /// <summary>The stored profile (a fresh copy), or null.</summary>
    public LearnerProfile? Peek(string profileId) =>
        _profiles.TryGetValue(profileId, out var bytes) ? JsonSerializer.Deserialize(bytes, SenseiJsonContext.Default.LearnerProfile) : null;

    /// <summary>The stored JSON text, or null.</summary>
    public string? RawJson(string profileId) =>
        _profiles.TryGetValue(profileId, out var bytes) ? System.Text.Encoding.UTF8.GetString(bytes) : null;

    public void Put(LearnerProfile profile) => _profiles[profile.ProfileId] = JsonSerializer.SerializeToUtf8Bytes(profile, SenseiJsonContext.Default.LearnerProfile);

    public Task<LearnerProfile?> LoadAsync(string profileId, CancellationToken ct)
    {
        Interlocked.Increment(ref _loads);
        return Task.FromResult(Peek(profileId));
    }

    public Task SaveAsync(LearnerProfile profile, CancellationToken ct)
    {
        if (FailSavesWith is not null) throw FailSavesWith;
        Interlocked.Increment(ref _saves);
        Put(profile);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string profileId, CancellationToken ct)
    {
        Interlocked.Increment(ref _deletes);
        _profiles.TryRemove(profileId, out _);
        return Task.CompletedTask;
    }
}

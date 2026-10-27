using System;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Storage;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Learner;

/// <summary>
/// Holds the live learner profile and persists it. Mutate <see cref="Profile"/> only under the engine lock;
/// <see cref="SaveAsync"/> snapshots under the lock and writes outside it, one write at a time.
/// </summary>
public sealed class LearnerBook
{
    private readonly object _sync;
    private readonly IProfileStore _store;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    internal LearnerBook(object sync, IProfileStore store, TimeProvider clock, LearnerProfile profile)
    {
        _sync = sync;
        _store = store;
        _clock = clock;
        Profile = profile;
    }

    /// <summary>The live profile. Read and write under the engine lock.</summary>
    public LearnerProfile Profile { get; internal set; }

    /// <summary>The current session's record.</summary>
    public SessionRecord? CurrentSession(string sessionId) => Profile.FindSession(sessionId);

    /// <summary>Deep copy of the live profile (takes the engine lock).</summary>
    public LearnerProfile Snapshot()
    {
        lock (_sync) return Clone(Profile);
    }

    private volatile bool _dirty;

    /// <summary>
    /// True when a save failed (or never ran) and the profile in memory is ahead of the stored one. Every later write saves the whole
    /// profile and clears it; disposal retries once more (S22).
    /// </summary>
    internal bool IsDirty => _dirty;

    /// <summary>Stamps <c>updated_at</c>, snapshots under the lock and saves through the store.</summary>
    public async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await _saveGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Snapshot inside the save gate so writes reach the store in commit order.
                LearnerProfile copy;
                lock (_sync)
                {
                    Profile.UpdatedAt = _clock.GetUtcNow();
                    copy = Clone(Profile);
                }
                await _store.SaveAsync(copy, ct).ConfigureAwait(false);
                _dirty = false;
            }
            finally
            {
                _saveGate.Release();
            }
        }
        catch (Exception ex)
        {
            // The change in memory stays (S22); only the write is owed. The store is named as the source of the failure.
            _dirty = true;
            FaultCategories.Tag(ex, FaultCategories.ProfileStore);
            throw;
        }
    }

    internal async Task DeleteAsync(string profileId, CancellationToken ct)
    {
        try
        {
            await _store.DeleteAsync(profileId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FaultCategories.Tag(ex, FaultCategories.ProfileStore);
            throw;
        }
    }

    /// <summary>Deep copy through the source-generated context.</summary>
    public static LearnerProfile Clone(LearnerProfile profile) => JsonCopy.Clone(profile, SenseiJsonContext.Default.LearnerProfile);

    /// <summary>The learner-facing deep copy of SPEC §3 <c>GetProfile</c>.</summary>
    public static LearnerProfileView ToView(LearnerProfile profile) =>
        JsonCopy.Convert(profile, SenseiJsonContext.Default.LearnerProfile, SenseiJsonContext.Default.LearnerProfileView);
}

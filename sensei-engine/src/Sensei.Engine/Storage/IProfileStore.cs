using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Learner;

namespace Sensei.Engine.Storage;

/// <summary>Learner profile persistence port (SPEC §10).</summary>
public interface IProfileStore
{
    Task<LearnerProfile?> LoadAsync(string profileId, CancellationToken ct);
    Task SaveAsync(LearnerProfile profile, CancellationToken ct);
    Task DeleteAsync(string profileId, CancellationToken ct);
}

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;

namespace Sensei.Engine.Storage;

/// <summary>
/// Stores <c>&lt;directory&gt;/&lt;profileId&gt;.json</c> through the source-generated context, writing a temp file and
/// renaming it over the target (S57).
/// </summary>
public sealed class FileProfileStore : IProfileStore
{
    private readonly string _directory;

    public FileProfileStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("directory is required", nameof(directory));
        _directory = directory;
    }

    public string Directory => _directory;

    public string PathFor(string profileId)
    {
        ProfileIds.Validate(profileId);
        return Path.Combine(_directory, profileId + ".json");
    }

    public async Task<LearnerProfile?> LoadAsync(string profileId, CancellationToken ct)
    {
        var path = PathFor(profileId);
        if (!File.Exists(path)) return null;
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        {
            bytes = new byte[stream.Length];
            var read = 0;
            while (read < bytes.Length)
            {
                var n = await stream.ReadAsync(bytes, read, bytes.Length - read, ct).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
        }
        try
        {
            var profile = JsonSerializer.Deserialize(bytes, SenseiJsonContext.Default.LearnerProfile);
            if (profile is null || profile.ProfileId != profileId || profile.Version != LearnerProfile.CurrentVersion)
                throw new ProfileCorruptException(profileId, path, null);
            return profile;
        }
        catch (JsonException ex)
        {
            throw new ProfileCorruptException(profileId, path, ex);
        }
        catch (NotSupportedException ex)
        {
            throw new ProfileCorruptException(profileId, path, ex);
        }
    }

    public async Task SaveAsync(LearnerProfile profile, CancellationToken ct)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        var path = PathFor(profile.ProfileId);
        System.IO.Directory.CreateDirectory(_directory);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, SenseiJsonContext.Default.LearnerProfile);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public Task DeleteAsync(string profileId, CancellationToken ct)
    {
        var path = PathFor(profileId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}

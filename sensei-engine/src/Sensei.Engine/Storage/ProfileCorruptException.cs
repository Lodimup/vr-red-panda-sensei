using System;
using System.IO;

namespace Sensei.Engine.Storage;

/// <summary>A stored profile that cannot be read (S57). The file is never silently replaced.</summary>
public sealed class ProfileCorruptException : Exception
{
    public ProfileCorruptException(string profileId, string path, Exception? inner)
        : base($"profile '{profileId}' at {Path.GetFileName(path)} is corrupt and was left untouched", inner)
    {
        ProfileId = profileId;
        FilePath = path;
    }

    public string ProfileId { get; }
    public string FilePath { get; }
}

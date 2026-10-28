using System;

namespace Sensei.Engine.Storage;

/// <summary>
/// A stored profile that cannot be read (S57). The file is never silently replaced. S77: the message is written by the engine and
/// holds no text a caller supplied (no profile id, no path); <see cref="ProfileId"/> and <see cref="FilePath"/> carry them for the
/// host that asked for that profile.
/// </summary>
public sealed class ProfileCorruptException : Exception
{
    /// <summary>The whole message; fixed.</summary>
    public const string FixedMessage = "the stored profile is corrupt and was left untouched";

    public ProfileCorruptException(string profileId, string path, Exception? inner)
        : base(FixedMessage, inner)
    {
        ProfileId = profileId;
        FilePath = path;
    }

    public string ProfileId { get; }
    public string FilePath { get; }
}

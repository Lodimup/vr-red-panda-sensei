using System;
using System.Text.RegularExpressions;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Storage;

/// <summary>Profile ids (S57): <c>^[a-z0-9_-]{1,40}$</c>.</summary>
public static class ProfileIds
{
    private static readonly Regex Pattern = new(@"^[a-z0-9_-]{1,40}\z", RegexOptions.CultureInvariant);

    public static bool IsValid(string? profileId) => profileId is not null && Pattern.IsMatch(profileId);

    /// <summary>Throws <see cref="ArgumentException"/> for an invalid id.</summary>
    public static void Validate(string? profileId, string paramName = "profileId")
    {
        if (!IsValid(profileId))
            throw Authored.Argument("profile id must match ^[a-z0-9_-]{1,40}$", paramName);
    }
}

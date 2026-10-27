using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Foundation;

public sealed class OverlappingCredentialPatterns
{
    [Fact]
    public void S78_overlapping_sk_pattern_does_not_expose_the_rest_of_a_long_credential()
    {
        // The ENTIRE string matches the mixed 32+ character run from S78.
        const string credential = "prefix9abcdefghijklmnopsk-abcdefghijklmnop";
        Assert.True(CredentialPattern.IsMatch(credential));

        Assert.Equal("[redacted]", CredentialPattern.Redact(credential));
    }
}

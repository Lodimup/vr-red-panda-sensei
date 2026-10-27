using System.Collections;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Foundation.RoundThree;

public sealed class CreateSecretEnumeration
{
    [Fact]
    public async Task S77_CreateAsync_covers_failure_while_copying_known_secrets()
    {
        const string credential = "sk-abcdefghijklmnop123456";
        var options = new SenseiEngineOptions
        {
            ChatClient = null,
            Speech = new RecordingSpeechOutput(),
            ProfileStore = new InMemoryProfileStore(),
            KnownSecrets = new FailingSecrets(credential),
        };
        var error = await Record.ExceptionAsync(() => SenseiEngine.CreateAsync(options));
        Assert.NotNull(error);
        Assert.DoesNotContain(credential, error.ToString());
        Assert.IsType<SenseiEngineException>(error);
    }

    private sealed class FailingSecrets(string credential) : IReadOnlyList<string>
    {
        public int Count => 1;
        public string this[int index] => credential;
        public IEnumerator<string> GetEnumerator()
        {
            yield return credential;
            throw new IOException("credential source failed: " + credential);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

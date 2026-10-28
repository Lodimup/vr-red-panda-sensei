using System.Threading;
using System.Threading.Tasks;

namespace Sensei.Voice
{
    public interface IRecognizer
    {
        Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct);
    }
}

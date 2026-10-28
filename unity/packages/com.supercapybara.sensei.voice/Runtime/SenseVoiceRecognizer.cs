using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SherpaOnnx;

namespace Sensei.Voice
{
    public sealed class SenseVoiceRecognizer : IRecognizer, IDisposable
    {
        private OfflineRecognizer native;
        private readonly SerialNativeWorker worker;
        private SenseVoiceRecognizer() { worker = new SerialNativeWorker("Sensei ASR", () => native?.Dispose()); }

        public static async Task<SenseVoiceRecognizer> LoadAsync(string modelDir, int numThreads = 2, bool useItn = true)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            var instance = new SenseVoiceRecognizer();
            try
            {
                await instance.worker.Enqueue(() =>
                {
                    foreach (string name in new[] { "model.int8.onnx", "tokens.txt" })
                    {
                        string path = Path.Combine(modelDir, name);
                        if (!File.Exists(path)) throw new FileNotFoundException("Missing SenseVoice asset", name);
                        if (new FileInfo(path).Length == 0) throw new IOException("Empty SenseVoice asset: " + name);
                    }
                    var config = new OfflineRecognizerConfig();
                    config.ModelConfig.NumThreads = numThreads;
                    config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
                    config.ModelConfig.SenseVoice.Model = Path.Combine(modelDir, "model.int8.onnx");
                    config.ModelConfig.SenseVoice.Language = "auto";
                    // Orchestrator decision 2026-10-28: classic SenseVoice defaults to ITN; optional Nano measurement disables it.
                    config.ModelConfig.SenseVoice.UseInverseTextNormalization = useItn ? 1 : 0;
                    instance.native = new OfflineRecognizer(config);
                    return true;
                }, CancellationToken.None);
                return instance;
            }
            catch { instance.Dispose(); throw; }
        }

        public Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct)
        {
            if (samples16k == null || samples16k.Length == 0) throw new ArgumentException("No microphone samples", nameof(samples16k));
            var samples = (float[])samples16k.Clone();
            return worker.Enqueue(() =>
            {
                using (var stream = native.CreateStream())
                {
                    stream.AcceptWaveform(16000, samples);
                    native.Decode(stream);
                    return stream.Result.Text.Trim();
                }
            }, ct);
        }

        public void Dispose() { worker.Dispose(); }
    }
}

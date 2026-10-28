using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SherpaOnnx;

namespace Sensei.Voice
{
    // The worker owns native construction, use, and destruction. Dispose never
    // blocks Unity's thread or frees an object while native inference uses it.
    internal sealed class SerialNativeWorker : IDisposable
    {
        private readonly BlockingCollection<Action> jobs = new BlockingCollection<Action>(16);
        private readonly Action cleanup;
        private readonly Thread thread;
        private int disposed;

        internal SerialNativeWorker(string name, Action cleanup)
        {
            this.cleanup = cleanup;
            thread = new Thread(Run) { IsBackground = true, Name = name };
            thread.Start();
        }

        internal Task<T> Enqueue<T>(Func<T> operation, CancellationToken ct)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ct.IsCancellationRequested) return Task.FromCanceled<T>(ct);
            Action job = () =>
            {
                if (ct.IsCancellationRequested) { result.TrySetCanceled(ct); return; }
                try
                {
                    T value = operation();
                    if (ct.IsCancellationRequested) result.TrySetCanceled(ct);
                    else result.TrySetResult(value);
                }
                catch (Exception ex) { result.TrySetException(ex); }
            };
            try
            {
                if (Volatile.Read(ref disposed) != 0 || !jobs.TryAdd(job))
                    result.TrySetException(new InvalidOperationException("Speech worker closed or queue full."));
            }
            catch (InvalidOperationException) { result.TrySetException(new ObjectDisposedException(nameof(SerialNativeWorker))); }
            return result.Task;
        }

        private void Run()
        {
            try { foreach (Action job in jobs.GetConsumingEnumerable()) job(); }
            finally { cleanup(); jobs.Dispose(); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) jobs.CompleteAdding();
        }
    }

    public sealed class LocalTts : IDisposable
    {
        // Orchestrator decision 2026-10-28: one supplied preset for both languages.
        public const int VoicePreset = 0;
        public const int QualitySteps = 8;
        private OfflineTts native;
        private readonly SerialNativeWorker worker;

        private LocalTts() { worker = new SerialNativeWorker("Sensei TTS", () => native?.Dispose()); }

        public static async Task<LocalTts> LoadAsync(string modelDir, int numThreads = 2)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            var instance = new LocalTts();
            try
            {
                await instance.worker.Enqueue(() =>
                {
                    string FilePath(string name)
                    {
                        string path = Path.Combine(modelDir, name);
                        if (!File.Exists(path)) throw new FileNotFoundException("Missing Supertonic asset", name);
                        if (new FileInfo(path).Length == 0) throw new IOException("Empty Supertonic asset: " + name);
                        return path;
                    }
                    var config = new OfflineTtsConfig();
                    config.Model.NumThreads = numThreads;
                    config.Model.Supertonic.DurationPredictor = FilePath("duration_predictor.int8.onnx");
                    config.Model.Supertonic.TextEncoder = FilePath("text_encoder.int8.onnx");
                    config.Model.Supertonic.VectorEstimator = FilePath("vector_estimator.int8.onnx");
                    config.Model.Supertonic.Vocoder = FilePath("vocoder.int8.onnx");
                    config.Model.Supertonic.TtsJson = FilePath("tts.json");
                    config.Model.Supertonic.UnicodeIndexer = FilePath("unicode_indexer.bin");
                    config.Model.Supertonic.VoiceStyle = FilePath("voice.bin");
                    // The pinned native runtime exits the process for invalid config;
                    // check the exact fields it requires before calling into it.
                    JObject metadata;
                    try { metadata = JObject.Parse(File.ReadAllText(config.Model.Supertonic.TtsJson)); }
                    catch (Newtonsoft.Json.JsonException) { throw new IOException("Invalid Supertonic JSON configuration"); }
                    foreach (string field in new[] { "ae.sample_rate", "ae.base_chunk_size", "ttl.chunk_compress_factor", "ttl.latent_dim" })
                    {
                        JToken value = metadata.SelectToken(field);
                        if (value == null || value.Type != JTokenType.Integer || !long.TryParse(value.ToString(), out long number) || number <= 0 || number > int.MaxValue)
                            throw new IOException("Invalid Supertonic configuration field: " + field);
                    }
                    instance.native = new OfflineTts(config);
                    return true;
                }, CancellationToken.None);
                return instance;
            }
            catch { instance.Dispose(); throw; }
        }

        public Task<TtsAudio> SynthesizeAsync(string text, string lang, float speed, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Speech text is empty", nameof(text));
            if (lang != "ja" && lang != "en") throw new ArgumentException("Expected ja or en", nameof(lang));
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
            int chars = 0;
            for (int i = 0; i < text.Length; ++i, ++chars)
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) ++i;
            // Orchestrator decision 2026-10-28: preserve SPEC section 8 scalar segment limits.
            if (chars > (lang == "ja" ? 60 : 160)) throw new ArgumentException("speech_limit_exceeded", nameof(text));
            return worker.Enqueue(() =>
            {
                var config = new OfflineTtsGenerationConfig { Sid = VoicePreset, Speed = speed, NumSteps = QualitySteps };
                config.Extra["lang"] = lang;
                var audio = native.GenerateWithConfig(text, config, null);
                try
                {
                    if (audio.Handle == IntPtr.Zero || audio.NumSamples <= 0) throw new InvalidOperationException("Supertonic returned no audio.");
                    return new TtsAudio(audio.Samples, audio.SampleRate);
                }
                finally { audio.Dispose(); }
            }, ct);
        }

        public void Dispose() { worker.Dispose(); }
    }
}

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Sensei.Voice
{
    public sealed class OpenRouterAudioRecognizer : IRecognizer
    {
        private readonly string apiKey;
        private readonly string model;
        public double? LastCost { get; private set; }
        public const string Instruction = "Transcribe this speech exactly as spoken. It may mix Japanese and English. Output only the transcript, Japanese in Japanese script.";

        public OpenRouterAudioRecognizer(string apiKey, string model)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Missing OpenRouter key", nameof(apiKey));
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Missing OpenRouter model", nameof(model));
            this.apiKey = apiKey;
            this.model = model;
        }

        public async Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct)
        {
            try { return await TranscribeCoreAsync(samples16k, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                string message = ex.Message.Replace(apiKey, "[redacted]");
                message = Regex.Replace(message, @"(?i)sk-or-\S+", "[redacted]");
                if (message.Length > 300) message = message.Substring(0, 300);
                // Do not retain the original exception as InnerException: it can contain a secret.
                throw new IOException("OpenRouter transcription failed: " + message);
            }
        }

        private async Task<string> TranscribeCoreAsync(float[] samples16k, CancellationToken ct)
        {
            if (samples16k == null || samples16k.Length == 0) throw new ArgumentException("No samples", nameof(samples16k));
            LastCost = null;
            ct.ThrowIfCancellationRequested();
            string audio = await Task.Run(() => Convert.ToBase64String(WaveAudio.Encode(samples16k, 16000)), ct);
            var body = new JObject {
                ["model"] = model,
                ["messages"] = new JArray(new JObject {
                    ["role"] = "user",
                    ["content"] = new JArray(
                        new JObject { ["type"] = "text", ["text"] = Instruction },
                        new JObject { ["type"] = "input_audio", ["input_audio"] = new JObject { ["data"] = audio, ["format"] = "wav" } })
                })
            };
            using (var request = new UnityWebRequest("https://openrouter.ai/api/v1/chat/completions", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 60;
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                request.SetRequestHeader("Content-Type", "application/json");
                await VoiceModelInstaller.SendAsync(request, ct);
                // Never include raw provider errors, headers, or request bodies in logs.
                var response = JObject.Parse(request.downloadHandler.text);
                JToken error = response["error"];
                if (error != null && error.Type != JTokenType.Null)
                {
                    var details = error as JObject;
                    int.TryParse((string)details?["code"], out int code);
                    if (code == 429) throw new IOException("Speech HTTP request failed: status 429");
                    string message = (string)details?["message"] ?? "no provider message";
                    message = message.Replace(apiKey, "[redacted]");
                    message = Regex.Replace(message, @"(?i)sk-or-\S+", "[redacted]");
                    if (message.Length > 300) message = message.Substring(0, 300);
                    throw new IOException("OpenRouter speech API error " + code + ": " + message);
                }
                LastCost = (double?)response["usage"]?["cost"];
                JToken content = response["choices"]?[0]?["message"]?["content"];
                string transcript = content?.Type == JTokenType.String ? (string)content : null;
                if (string.IsNullOrWhiteSpace(transcript)) throw new IOException("OpenRouter returned an empty transcript");
                if (transcript.Contains(apiKey) || Regex.IsMatch(transcript, @"(?i)sk-or-\S+")) throw new IOException("Provider returned credential-shaped transcript");
                return transcript.Trim();
            }
        }
    }

    public static class WaveAudio
    {
        public static byte[] Encode(float[] samples, int sampleRate)
        {
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
                foreach (float value in samples) writer.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, value)) * 32767f));
                return bytes.ToArray();
            }
        }

        public static TtsAudio Decode(byte[] bytes)
        {
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new IOException("Expected RIFF WAV");
                reader.ReadInt32();
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new IOException("Expected WAV");
                int rate = 0, channels = 0, bits = 0, format = 0;
                while (reader.BaseStream.Position + 8 <= bytes.Length)
                {
                    string id = Encoding.ASCII.GetString(reader.ReadBytes(4)); int length = reader.ReadInt32();
                    long end = reader.BaseStream.Position + length;
                    if (length < 0 || end > bytes.Length) throw new IOException("Invalid WAV chunk");
                    if (id == "fmt ")
                    {
                        format = reader.ReadInt16(); channels = reader.ReadInt16(); rate = reader.ReadInt32();
                        reader.ReadInt32(); reader.ReadInt16(); bits = reader.ReadInt16();
                    }
                    if (id == "data")
                    {
                        if (format != 1 || bits != 16 || channels < 1 || rate <= 0) throw new IOException("Expected PCM16 WAV");
                        var samples = new float[length / (2 * channels)];
                        for (int i = 0; i < samples.Length; ++i)
                            for (int c = 0; c < channels; ++c) samples[i] += reader.ReadInt16() / (32768f * channels);
                        return new TtsAudio(samples, rate);
                    }
                    reader.BaseStream.Position = end + (length & 1);
                }
                throw new IOException("WAV has no audio data");
            }
        }

        public static float[] Resample(float[] samples, int fromRate, int toRate)
        {
            if (fromRate <= 0 || toRate <= 0) throw new ArgumentOutOfRangeException(nameof(fromRate));
            if (fromRate == toRate || samples.Length == 0) return samples;
            int size = (int)((long)samples.Length * toRate / fromRate);
            var result = new float[size];
            for (int i = 0; i < size; ++i)
            {
                double at = (double)i * fromRate / toRate;
                int lo = (int)at; int hi = Math.Min(lo + 1, samples.Length - 1);
                result[i] = (float)(samples[lo] + (samples[hi] - samples[lo]) * (at - lo));
            }
            return result;
        }
    }
}

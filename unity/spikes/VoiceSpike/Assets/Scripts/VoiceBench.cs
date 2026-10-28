using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sensei.Voice;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

public sealed class VoiceBench : MonoBehaviour
{
    private readonly List<double> frameTimes = new List<double>();
    private bool measuring;
    private long lastFrame;
    private SpeechPlayer player;
    private string status = "Starting voice benchmark";
    private static readonly string[] Texts = {
        "こんにちは。わたしはレッサーパンダせんせいです。",
        "りんごを一つください。",
        "かばんの中に鍵があります。",
        "Hello! I'm Red Panda Sensei. Let's learn some Japanese together.",
        "Say it after me, slowly.",
        "こんにちは。わたしはレッサーパンダせんせいです。"
    };

    private void Update()
    {
        long now = Stopwatch.GetTimestamp();
        if (measuring && lastFrame != 0) frameTimes.Add((now - lastFrame) * 1000.0 / Stopwatch.Frequency);
        lastFrame = now;
        // Keep visible rendering active throughout inference, including on a 2D Quest panel.
        Camera.main.backgroundColor = Color.Lerp(new Color(0.12f, 0.2f, 0.25f), new Color(0.2f, 0.3f, 0.36f), (Mathf.Sin(Time.unscaledTime) + 1f) * 0.5f);
    }

    private void OnGUI()
    {
        GUI.skin.label.fontSize = 28;
        GUI.Label(new Rect(32, 32, Screen.width - 64, Screen.height - 64), "Red Panda Sensei — Voice benchmark\n" + status);
    }

    private async void Start()
    {
        Application.runInBackground = true;
        Application.targetFrameRate = 72;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        player = gameObject.AddComponent<SpeechPlayer>();
        try
        {
            if (ReadSetting("RPS_MIC_PROBE") == "1") await RunMicProbeAsync();
            else await RunAsync();
        }
        catch (Exception ex)
        {
            status = "Benchmark failed: " + ex.GetType().Name;
            // Controlled exception messages never contain the env file or provider body.
            Log("error " + ex.GetType().Name + " " + ex.Message);
        }
    }

    private async Task RunAsync()
    {
        var token = destroyCancellationToken;
        var sw = Stopwatch.StartNew();
        string ttsDir = await VoiceModelInstaller.EnsureInstalledAsync("supertonic3", "2026-05-11", token);
        LogJson("install", new { model = "supertonic3", ms = sw.Elapsed.TotalMilliseconds });
        sw.Restart();
        string asrDir = await VoiceModelInstaller.EnsureInstalledAsync("sensevoice", "2025-09-09", token);
        LogJson("install", new { model = "sensevoice", ms = sw.Elapsed.TotalMilliseconds });
        sw.Restart();
        string nanoDir = null;
        try
        {
            nanoDir = await VoiceModelInstaller.EnsureInstalledAsync("sensevoice-nano", "2025-12-17", token);
            LogJson("install", new { model = "sensevoice-nano", ms = sw.Elapsed.TotalMilliseconds });
        }
        catch (FileNotFoundException) { LogJson("asr_optional_skipped", new { model = "sensevoice-nano", reason = "optional model not bundled" }); }
        sw.Restart();
        using (var tts = await LocalTts.LoadAsync(ttsDir))
        {
            LogJson("tts_load", new { ms = sw.Elapsed.TotalMilliseconds, threads = 2, preset = LocalTts.VoicePreset, steps = LocalTts.QualitySteps });
            var generated = new List<TtsAudio>();
            var allFrames = new List<double>();
            string output = Path.Combine(Application.persistentDataPath, "tts-out");
            Directory.CreateDirectory(output);
            player.StopAll(1);
            await Task.Yield();
            for (int i = 0; i < Texts.Length; ++i)
            {
                token.ThrowIfCancellationRequested();
                string lang = i == 3 || i == 4 ? "en" : "ja";
                float speed = i == 5 ? 0.8f : 1f;
                status = "Offline synthesis " + (i + 1) + "/6";
                frameTimes.Clear();
                lastFrame = Stopwatch.GetTimestamp();
                measuring = true;
                sw.Restart();
                TtsAudio audio;
                try { audio = await tts.SynthesizeAsync(Texts[i], lang, speed, token); }
                finally { measuring = false; }
                double ms = sw.Elapsed.TotalMilliseconds;
                generated.Add(audio);
                double seconds = (double)audio.Samples.Length / audio.SampleRate;
                LogJson("tts", new { index = i, lang, speed, text = Texts[i], chars = ScalarCount(Texts[i]), ms, seconds, rtf = ms / (seconds * 1000), sample_rate = audio.SampleRate });
                allFrames.AddRange(frameTimes);
                LogFrames("tts_frames", i, frameTimes);
                await Task.Run(() => File.WriteAllBytes(Path.Combine(output, "tts-" + i + ".wav"), WaveAudio.Encode(audio.Samples, audio.SampleRate)), token);
                if (!await player.PlayAsync(audio, 1, token)) throw new InvalidOperationException("TTS playback was cancelled unexpectedly");
            }
            LogFrames("tts_frames_total", -1, allFrames);
            var longest = generated.OrderByDescending(a => (double)a.Samples.Length / a.SampleRate).First();
            Task<bool> playback = player.PlayAsync(longest, 1, token);
            while (!player.IsPlaying) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
            await Task.Delay(1000, token);
            sw.Restart();
            player.StopAll(2);
            while (player.IsPlaying) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
            double stopMs = sw.Elapsed.TotalMilliseconds;
            bool interrupted = !await playback;
            bool stalePlayed = await player.PlayAsync(longest, 1, token);
            LogJson("interrupt", new { ms = stopMs, interrupted, stale_played = stalePlayed, is_playing = player.IsPlaying });
            if (!interrupted || stalePlayed) throw new InvalidOperationException("Interrupt/stale audio gate failed");
            Log("tts_complete");
            status = "TTS complete. Waiting for host memory probe and Wi-Fi.";
            string ready = Path.Combine(Application.persistentDataPath, "online.ready");
            while (!File.Exists(ready)) { token.ThrowIfCancellationRequested(); await Task.Delay(100, token); }
            // Wi-Fi associating takes a few seconds after svc wifi enable.
            await Task.Delay(7000, token);
            var models = await SelectModels(token);
            string key = ReadKey();
            sw.Restart();
            using (var local = await SenseVoiceRecognizer.LoadAsync(asrDir))
            {
                // Orchestrator decision 2026-10-28: classic SenseVoice uses in-graph ITN; Nano is an optional ITN-off comparison.
                LogJson("asr_load", new { ms = sw.Elapsed.TotalMilliseconds, model = "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09", threads = 2, language = "auto", itn = true });
                sw.Restart();
                SenseVoiceRecognizer optionalNano = null;
                if (nanoDir != null)
                {
                    try
                    {
                        optionalNano = await SenseVoiceRecognizer.LoadAsync(nanoDir, useItn: false);
                        LogJson("asr_load", new { ms = sw.Elapsed.TotalMilliseconds, model = "sherpa-onnx-sense-voice-funasr-nano-int8-2025-12-17", threads = 2, language = "auto", itn = false });
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException)) { LogJson("asr_optional_skipped", new { model = "sensevoice-nano", reason = ex.Message }); }
                }
                using var nano = optionalNano;
                var recognizers = new List<(string name, IRecognizer recognizer)> { ("sensevoice", local) };
                if (nano != null) recognizers.Add(("sensevoice-nano", nano));
                foreach (string model in models) recognizers.Add((model, new OpenRouterAudioRecognizer(key, model)));
                key = null;
                string corpus = Encoding.UTF8.GetString(await ReadAsset("asr-corpus/corpus.tsv", token));
                var measurements = new List<(string name, string group, double cer, double ms)>();
                var consecutiveFailures = new Dictionary<string, int>();
                var disabledRoutes = new HashSet<string>();
                int failedRequests = 0;
                foreach (string line in corpus.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cells = line.TrimEnd('\r').Split('\t');
                    if (cells.Length != 2) throw new IOException("Invalid corpus TSV");
                    string file = cells[0], expected = cells[1];
                    var clip = WaveAudio.Decode(await ReadAsset("asr-corpus/" + file, token));
                    float[] samples = WaveAudio.Resample(clip.Samples, clip.SampleRate, 16000);
                    foreach (var route in recognizers)
                    {
                        status = "ASR " + file + "\n" + route.name;
                        if (disabledRoutes.Contains(route.name))
                        {
                            LogJson("asr_skipped", new { model = route.name, file, reason = "three consecutive errors" });
                            continue;
                        }
                        sw.Restart();
                        try
                        {
                            string transcript = await TranscribeWithRateLimitRetry(route.recognizer, samples, token);
                            double ms = sw.Elapsed.TotalMilliseconds;
                            double cer = Cer(expected, transcript);
                            string group = file.Substring(0, file.IndexOf('-'));
                            measurements.Add((route.name, group, cer, ms));
                            consecutiveFailures[route.name] = 0;
                            LogJson("asr", new { model = route.name, file, group, expected, transcript, ms, cer, cost = (route.recognizer as OpenRouterAudioRecognizer)?.LastCost });
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            ++failedRequests;
                            int count = consecutiveFailures.TryGetValue(route.name, out int previous) ? previous + 1 : 1;
                            consecutiveFailures[route.name] = count;
                            if (count >= 3) disabledRoutes.Add(route.name);
                            LogJson("asr_error", new { model = route.name, file, ms = sw.Elapsed.TotalMilliseconds, error = ex.Message, consecutive = count });
                        }
                    }
                }
                foreach (var group in measurements.GroupBy(m => (m.name, m.group)))
                {
                    var latencies = group.Select(m => m.ms).OrderBy(x => x).ToList();
                    LogJson("asr_summary", new { model = group.Key.name, group = group.Key.group, count = latencies.Count, mean_cer = group.Average(m => m.cer), p50_ms = Percentile(latencies, 0.5), p95_ms = Percentile(latencies, 0.95) });
                }
                // Orchestrator decision 2026-10-28: play the corpus from the nearby Mac and measure actual Quest microphone input.
                var mic = gameObject.AddComponent<MicCapture>();
                mic.DeviceName = ReadSetting("RPS_MIC_DEVICE");
                var micMeasurements = new List<(string name, string group, double cer, double ms)>();
                int corpusIndex = 0;
                foreach (string line in corpus.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cells = line.TrimEnd('\r').Split('\t');
                    if (cells.Length != 2) throw new IOException("Invalid corpus TSV");
                    string file = cells[0], expected = cells[1];
                    status = "Microphone " + file;
                    mic.Begin();
                    var startup = Stopwatch.StartNew();
                    while (!mic.IsRecording)
                    {
                        token.ThrowIfCancellationRequested();
                        if (startup.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("Quest microphone did not start; check RECORD_AUDIO permission");
                        await Task.Yield();
                    }
                    Log("mic_ready i=" + corpusIndex);
                    await Task.Delay(5000, token);
                    float[] samples = mic.End();
                    if (samples.Length == 0) throw new InvalidOperationException("Quest microphone returned no samples");
                    double rms = Math.Sqrt(samples.Select(x => (double)x * x).Average());
                    LogJson("mic_capture", new { index = corpusIndex, file, samples = samples.Length, sample_rate = 16000, rms });
                    foreach (var route in recognizers)
                    {
                        if (disabledRoutes.Contains(route.name))
                        {
                            LogJson("mic_skipped", new { index = corpusIndex, model = route.name, file, reason = "three consecutive errors" });
                            continue;
                        }
                        sw.Restart();
                        try
                        {
                            string transcript = await TranscribeWithRateLimitRetry(route.recognizer, samples, token);
                            double ms = sw.Elapsed.TotalMilliseconds;
                            double cer = Cer(expected, transcript);
                            string group = file.Substring(0, file.IndexOf('-'));
                            micMeasurements.Add((route.name, group, cer, ms));
                            consecutiveFailures[route.name] = 0;
                            LogJson("mic", new { index = corpusIndex, model = route.name, file, group, expected, transcript, ms, cer, cost = (route.recognizer as OpenRouterAudioRecognizer)?.LastCost });
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            ++failedRequests;
                            int count = consecutiveFailures.TryGetValue(route.name, out int previous) ? previous + 1 : 1;
                            consecutiveFailures[route.name] = count;
                            if (count >= 3) disabledRoutes.Add(route.name);
                            LogJson("mic_error", new { index = corpusIndex, model = route.name, file, ms = sw.Elapsed.TotalMilliseconds, error = ex.Message, consecutive = count });
                        }
                    }
                    ++corpusIndex;
                }
                foreach (var group in micMeasurements.GroupBy(m => (m.name, m.group)))
                {
                    var latencies = group.Select(m => m.ms).OrderBy(x => x).ToList();
                    LogJson("mic_summary", new { model = group.Key.name, group = group.Key.group, count = latencies.Count, mean_cer = group.Average(m => m.cer), p50_ms = Percentile(latencies, 0.5), p95_ms = Percentile(latencies, 0.95) });
                }
                for (int i = 0; i < generated.Count; ++i)
                {
                    sw.Restart();
                    TtsAudio audio = generated[i];
                    string transcript = await local.TranscribeAsync(WaveAudio.Resample(audio.Samples, audio.SampleRate, 16000), token);
                    LogJson("roundtrip", new { index = i, expected = Texts[i], transcript, cer = Cer(Texts[i], transcript), ms = sw.Elapsed.TotalMilliseconds });
                }
                if (failedRequests > 0) throw new IOException("ASR benchmark had " + failedRequests + " failed requests; see asr_error/mic_error lines");
            }
            double maxFrame = allFrames.Max();
            bool frameGate = maxFrame <= 50;
            bool interruptGate = stopMs <= maxFrame && interrupted && !stalePlayed;
            LogJson("gates", new { frames_max_ms = maxFrame, frame_gate = frameGate, interrupt_ms = stopMs, interrupt_gate = interruptGate, stale_rejected = !stalePlayed });
            if (!frameGate || !interruptGate) throw new InvalidOperationException("Offline TTS frame or interrupt gate failed");
        }
        status = "Benchmark complete";
        Log("done");
    }

    private static async Task<string> TranscribeWithRateLimitRetry(IRecognizer recognizer, float[] samples, CancellationToken ct)
    {
        for (int attempt = 0; ; ++attempt)
        {
            try { return await recognizer.TranscribeAsync(samples, ct); }
            catch (IOException ex) when (ex.Message.EndsWith("status 429", StringComparison.Ordinal) && attempt < 2)
            {
                LogJson("rate_limit", new { attempt = attempt + 1 });
                await Task.Delay((attempt + 1) * 2000, ct);
            }
        }
    }

    private static string ReadKey()
    {
        return ReadSetting("OPENROUTER_API_KEY") ?? throw new IOException("OpenRouter key missing from provisioned env file");
    }

    private static string ReadSetting(string name)
    {
        string path = Path.Combine(Application.persistentDataPath, "sensei.env");
        string prefix = name + "=";
        foreach (string line in File.ReadAllLines(path))
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return line.Substring(prefix.Length).Trim();
        return null;
    }

    // Orchestrator decision 2026-10-28: test the default Quest microphone, then a named device if default is silent.
    private async Task RunMicProbeAsync()
    {
        var token = destroyCancellationToken;
        string[] devices = Microphone.devices;
        LogJson("mic_devices", new { devices });
        var mic = gameObject.AddComponent<MicCapture>();
        string asrDir = await VoiceModelInstaller.EnsureInstalledAsync("sensevoice", "2025-09-09", token);
        using var recognizer = await SenseVoiceRecognizer.LoadAsync(asrDir);
        for (int attempt = 0; attempt < 2; ++attempt)
        {
            if (attempt == 1 && devices.Length == 0) { LogJson("mic_probe_unavailable", new { reason = "no named microphone device" }); break; }
            string mode = attempt == 0 ? "default" : "named";
            mic.DeviceName = attempt == 0 ? null : devices[0];
            mic.Begin();
            var startup = Stopwatch.StartNew();
            while (!mic.IsRecording)
            {
                token.ThrowIfCancellationRequested();
                if (startup.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("Quest microphone did not start");
                await Task.Yield();
            }
            Log("mic_probe_ready mode=" + mode);
            await Task.Delay(5000, token);
            float[] samples = mic.End();
            double rms = samples.Length == 0 ? 0 : Math.Sqrt(samples.Select(x => (double)x * x).Average());
            string transcript = samples.Length == 0 ? "" : await recognizer.TranscribeAsync(samples, token);
            LogJson("mic_probe", new { mode, device = mic.DeviceName ?? "default", samples = samples.Length, sample_rate = 16000, rms, transcript, cer = Cer("りんごをください", transcript) });
            if (rms > 0) break;
        }
        string output = Path.Combine(Application.persistentDataPath, "tts-out");
        for (int i = 0; i < Texts.Length; ++i)
        {
            string path = Path.Combine(output, "tts-" + i + ".wav");
            TtsAudio audio = WaveAudio.Decode(await Task.Run(() => File.ReadAllBytes(path), token));
            var sw = Stopwatch.StartNew();
            string transcript = await recognizer.TranscribeAsync(WaveAudio.Resample(audio.Samples, audio.SampleRate, 16000), token);
            LogJson("roundtrip", new { index = i, expected = Texts[i], transcript, cer = Cer(Texts[i], transcript), ms = sw.Elapsed.TotalMilliseconds });
        }
        Log("mic_probe_done");
    }

    private static async Task<List<string>> SelectModels(CancellationToken ct)
    {
        byte[] bytes = await Download("https://openrouter.ai/api/v1/models", ct);
        var data = (JArray)JObject.Parse(Encoding.UTF8.GetString(bytes))["data"];
        // Orchestrator decision 2026-10-28: exclude :free models; compare the cheapest paid audio-input model and paid Gemini Flash.
        var audio = data.OfType<JObject>().Where(m => m["architecture"]?["input_modalities"] is JArray inputs && inputs.Any(x => (string)x == "audio"))
            .Where(m => !((string)m["id"]).Contains(":free") && Price(m, "prompt") >= 0 && Price(m, "completion") >= 0 && (Price(m, "prompt") > 0 || Price(m, "completion") > 0))
            .ToList();
        // Orchestrator decision 2026-10-28: benchmark exactly these two paid audio models.
        var requested = new[] { "mistralai/voxtral-small-24b-2507", "google/gemini-2.5-flash-lite" };
        var selected = new List<string>();
        foreach (string id in requested)
        {
            JObject model = audio.FirstOrDefault(m => (string)m["id"] == id)
                ?? throw new IOException("Required paid audio model unavailable: " + id);
            selected.Add(id);
            LogJson("openrouter_model", new { id, pricing = model["pricing"], selection = "two paid models specified by the orchestrator" });
        }
        return selected;
    }

    private static double Price(JObject model, string field) => double.TryParse((string)model["pricing"]?[field], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : double.PositiveInfinity;

    private static Task<byte[]> ReadAsset(string relative, CancellationToken ct)
    {
        string path = Application.streamingAssetsPath + "/" + relative;
        return path.Contains("://") ? Download(path, ct) : Task.Run(() => File.ReadAllBytes(path), ct);
    }

    private static async Task<byte[]> Download(string url, CancellationToken ct)
    {
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 30;
            var operation = request.SendWebRequest();
            while (!operation.isDone) { if (ct.IsCancellationRequested) { request.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
            ct.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success) throw new IOException("Benchmark download failed: status " + request.responseCode);
            return request.downloadHandler.data;
        }
    }

    private static int ScalarCount(string value)
    {
        int count = 0;
        for (int i = 0; i < value.Length; ++i, ++count)
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) ++i;
        return count;
    }

    // NFKC; remove Unicode whitespace and punctuation; compare Unicode scalar values.
    private static int[] Normalize(string value)
    {
        value = value.Normalize(NormalizationForm.FormKC);
        var scalars = new List<int>();
        for (int i = 0; i < value.Length; ++i)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(value, i);
            bool punctuation = category == UnicodeCategory.ConnectorPunctuation || category == UnicodeCategory.DashPunctuation || category == UnicodeCategory.OpenPunctuation || category == UnicodeCategory.ClosePunctuation || category == UnicodeCategory.InitialQuotePunctuation || category == UnicodeCategory.FinalQuotePunctuation || category == UnicodeCategory.OtherPunctuation;
            if (!char.IsWhiteSpace(value, i) && !punctuation) scalars.Add(char.ConvertToUtf32(value, i));
            if (char.IsHighSurrogate(value[i])) ++i;
        }
        return scalars.ToArray();
    }

    private static double Cer(string expected, string actual)
    {
        int[] a = Normalize(expected), b = Normalize(actual);
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; ++i)
        {
            int previous = row[0]; row[0] = i;
            for (int j = 1; j <= b.Length; ++j)
            {
                int old = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = old;
            }
        }
        return (double)row[b.Length] / Math.Max(1, a.Length);
    }

    private static double Percentile(List<double> sorted, double fraction) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * fraction) - 1)];
    private static void LogFrames(string label, int index, List<double> times)
    {
        var sorted = times.OrderBy(x => x).ToList();
        if (sorted.Count == 0) throw new InvalidOperationException("No rendered frames sampled during inference");
        LogJson(label, new { index, count = sorted.Count, p50_ms = Percentile(sorted, 0.5), p95_ms = Percentile(sorted, 0.95), max_ms = sorted.Last() });
    }
    private static void LogJson(string label, object value) => Log(label + " " + JsonConvert.SerializeObject(value));
    private static void Log(string message) => Debug.Log("RPS_VOICE " + message);
}

#if UNITY_EDITOR
public static class VoiceSpikeBuild
{
    public static void Build()
    {
        UnityEditor.PlayerSettings.companyName = "Supercapybara";
        UnityEditor.PlayerSettings.productName = "VoiceSpike";
        UnityEditor.PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.supercapybara.voicespike");
        UnityEditor.PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.ScriptingImplementation.IL2CPP);
        UnityEditor.PlayerSettings.Android.targetArchitectures = UnityEditor.AndroidArchitecture.ARM64;
        UnityEditor.PlayerSettings.Android.minSdkVersion = (UnityEditor.AndroidSdkVersions)32;
        UnityEditor.PlayerSettings.Android.targetSdkVersion = UnityEditor.AndroidSdkVersions.AndroidApiLevelAuto;
        UnityEditor.PlayerSettings.Android.forceInternetPermission = true;
        UnityEditor.PlayerSettings.Android.forceSDCardPermission = false;
        UnityEditor.PlayerSettings.Android.preferredInstallLocation = UnityEditor.AndroidPreferredInstallLocation.Auto;
        UnityEditor.PlayerSettings.Android.applicationEntry = UnityEditor.AndroidApplicationEntry.Activity;
        UnityEditor.PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.ManagedStrippingLevel.Low);
        UnityEditor.EditorUserBuildSettings.buildAppBundle = false;
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        var camera = new GameObject("Camera");
        camera.tag = "MainCamera";
        camera.AddComponent<Camera>();
        camera.AddComponent<AudioListener>();
        new GameObject("VoiceBench").AddComponent<VoiceBench>();
        Directory.CreateDirectory("Assets/Scenes");
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/Scenes/VoiceBench.unity");
        UnityEditor.AssetDatabase.SaveAssets();
        var result = UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/VoiceBench.unity" }, locationPathName = "Build/VoiceSpike.apk", target = UnityEditor.BuildTarget.Android,
            options = UnityEditor.BuildOptions.Development
        });
        if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("VoiceSpike Android build failed");
    }
}
#endif

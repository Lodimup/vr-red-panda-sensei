#nullable enable
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sensei.App.Core;
using Sensei.Unity;
using Sensei.Voice;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Sensei.App
{
    public sealed class PushToTalk : MonoBehaviour
    {
        private EngineHost host = null!; private SenseiView sensei = null!; private MicCapture mic = null!; private YouSaidLine line = null!; private SenseVoiceRecognizer? recognizer;
        private InputAction button = null!; private string? testAudio; private bool debug; private string? secret; private bool held; private bool micReady; private float started; private int captureEpoch;
        public void Initialize(EngineHost h, SenseiView s, YouSaidLine l, SenseVoiceRecognizer? r, string? audio, bool debugText, string? key)
        {
            host = h; sensei = s; line = l; recognizer = r; testAudio = audio; debug = debugText; secret = key; mic = gameObject.AddComponent<MicCapture>();
            button = new InputAction(type: InputActionType.Button, binding: "<XRController>{RightHand}/primaryButton"); button.performed += _ => Press(); button.canceled += _ => Release(); button.Enable();
        }
        public void StartTest(float seconds, int interruptMs) { StartCoroutine(Test(seconds, interruptMs)); }
        private IEnumerator Test(float seconds, int interruptMs)
        {
            if (interruptMs > 0) { while (!host.SpeechPlaying) yield return null; yield return new WaitForSeconds(interruptMs / 1000f); }
            else while (!host.VoiceIdle) yield return null;
            Press(); yield return new WaitForSeconds(seconds > 0 ? seconds : 0.5f); Release();
        }
        private void Update()
        {
            if (held && !micReady && string.IsNullOrEmpty(testAudio) && mic.IsRecording) { micReady = true; Debug.Log("RPS_MIC ready"); }
        }
        private void Press()
        {
            if (held || !host.CanTalk || recognizer == null) return;
            held = true; micReady = false; captureEpoch++; started = Time.realtimeSinceStartup; Debug.Log("RPS_PTT start"); host.InterruptSpeech(); sensei.SetTalkHeld(true); line.Set("Listening…");
            if (!string.IsNullOrEmpty(testAudio)) return;
            try { mic.Begin(); } catch (Exception e) { Debug.LogError("RPS_MIC fail type=" + e.GetType().Name); held = false; sensei.SetTalkHeld(false); line.Set("Couldn't hear that"); }
        }
        private async void Release()
        {
            if (!held) return;
            held = false; Debug.Log("RPS_PTT end"); sensei.SetTalkHeld(false); int epoch = captureEpoch; float seconds = Time.realtimeSinceStartup - started;
            if (seconds < TranscriptGate.MinHoldSeconds) { if (string.IsNullOrEmpty(testAudio)) try { mic.End(); } catch { } line.Set("Didn't catch that"); return; }
            line.Set("…");
            try
            {
                float[] samples;
                if (!string.IsNullOrEmpty(testAudio))
                {
                    string filename = testAudio; testAudio = null;
                    if (Path.GetFileName(filename) != filename) throw new IOException("Invalid test audio name");
                    using var request = UnityWebRequest.Get(Application.streamingAssetsPath + "/" + filename);
                    var operation = request.SendWebRequest(); while (!operation.isDone) await Task.Yield();
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException("Cannot read test audio");
                    var wave = WaveAudio.Decode(request.downloadHandler.data); samples = WaveAudio.Resample(wave.Samples, wave.SampleRate, 16000);
                }
                else samples = mic.End();
                if (debug) { double energy = 0; foreach (var sample in samples) energy += sample * sample; Debug.Log($"RPS_MIC samples={samples.Length} rms={(samples.Length == 0 ? 0 : Math.Sqrt(energy / samples.Length)):F6}"); }
                var watch = Stopwatch.StartNew(); var transcript = await recognizer!.TranscribeAsync(samples, CancellationToken.None);
                if (epoch != captureEpoch) return;
                string log = $"RPS_ASR ms={watch.ElapsedMilliseconds} chars={transcript.Length}";
                if (debug) log += " text=" + LogRedactor.Redact(transcript, secret).Replace('\n', ' ').Replace('\r', ' ');
                Debug.Log(log);
                if (!TranscriptGate.ShouldSend(seconds, transcript)) { line.Set("Didn't catch that"); return; }
                line.Set("You: " + transcript); await host.RunTranscript(transcript);
            }
            catch (Exception e) { Debug.LogError("RPS_ASR fail type=" + e.GetType().Name); if (epoch == captureEpoch) line.Set("Couldn't hear that"); }
        }
        private void OnDestroy() { button?.Dispose(); }
    }
}

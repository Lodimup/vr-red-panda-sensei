using System;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Sensei.Voice
{
    public sealed class MicCapture : MonoBehaviour
    {
        private AudioClip clip;
        private string device;
        private bool permissionPending;
        private int permissionRequest;
        // Null selects Unity's default device; a name explicitly selects one enumerated device.
        public string DeviceName { get; set; }
        // Wait for captured samples, not just device startup, before timing a recording.
        public bool IsRecording => clip != null && Microphone.IsRecording(device) && Microphone.GetPosition(device) > 0;

        public void Begin()
        {
            if (clip != null || permissionPending) throw new InvalidOperationException("Microphone already started or permission pending");
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                permissionPending = true;
                int request = ++permissionRequest;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => { if (request != permissionRequest) return; permissionPending = false; if (this != null && isActiveAndEnabled) StartCapture(); };
                callbacks.PermissionDenied += _ => { if (request != permissionRequest) return; permissionPending = false; Debug.LogError("Microphone permission denied"); };
                Permission.RequestUserPermission(Permission.Microphone, callbacks);
                return;
            }
#endif
            StartCapture();
        }

        private void StartCapture()
        {
            if (Microphone.devices.Length == 0) throw new InvalidOperationException("No microphone available");
            device = string.IsNullOrWhiteSpace(DeviceName) ? null : DeviceName;
            clip = Microphone.Start(device, false, 120, 16000);
            if (clip == null) throw new InvalidOperationException("Microphone failed to start");
        }

        public float[] End()
        {
            if (clip == null) { permissionPending = false; ++permissionRequest; throw new InvalidOperationException("Microphone is not recording"); }
            int count = Microphone.GetPosition(device);
            if (!Microphone.IsRecording(device) && count == 0) count = clip.samples;
            Microphone.End(device);
            var input = new float[count * clip.channels];
            if (count > 0 && !clip.GetData(input, 0)) throw new InvalidOperationException("Could not read microphone samples");
            var mono = new float[count];
            for (int i = 0; i < count; ++i)
                for (int c = 0; c < clip.channels; ++c) mono[i] += input[i * clip.channels + c] / clip.channels;
            int rate = clip.frequency;
            Destroy(clip);
            clip = null;
            return WaveAudio.Resample(mono, rate, 16000);
        }

        public void Cancel()
        {
            permissionPending = false; ++permissionRequest;
            if (clip == null) return;
            Microphone.End(device);
            Destroy(clip);
            clip = null;
        }

        private void OnDisable() => Cancel();
    }
}

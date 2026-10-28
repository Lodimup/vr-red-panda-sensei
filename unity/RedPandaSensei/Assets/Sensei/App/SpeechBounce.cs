using Sensei.Voice;
using UnityEngine;

namespace Sensei.App
{
    public sealed class SpeechBounce : MonoBehaviour
    {
        private SpeechPlayer player = null!; private SenseiView sensei = null!; private AudioSource source = null!; private float next;
        private readonly float[] samples = new float[2048];
        public void Initialize(SpeechPlayer p, SenseiView s) { player = p; sensei = s; source = p.GetComponent<AudioSource>(); }
        private void Update()
        {
            if (!player.IsPlaying || Time.time < next) return;
            next = Time.time + 0.05f; source.GetOutputData(samples, 0);
            double sum = 0; foreach (float sample in samples) sum += sample * sample;
            sensei.OnSpeechPeak((float)System.Math.Sqrt(sum / samples.Length));
        }
    }
}

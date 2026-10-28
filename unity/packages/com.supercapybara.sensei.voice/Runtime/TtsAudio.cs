using System;

namespace Sensei.Voice
{
    public readonly struct TtsAudio
    {
        public readonly float[] Samples;
        public readonly int SampleRate;

        public TtsAudio(float[] samples, int sampleRate)
        {
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            SampleRate = sampleRate;
        }
    }
}

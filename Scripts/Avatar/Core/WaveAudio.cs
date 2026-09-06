using System;
using System.Threading;
using ChatdollKit.SpeechPipeline.TTS;

namespace ChatdollKit.Avatar
{
    /// <summary>Unity-independent decoded PCM. Samples are interleaved and normalized to [-1, 1].</summary>
    public sealed class WaveAudio
    {
        public float[] Samples { get; }
        public int Channels { get; }
        public int SampleRate { get; }
        public int FrameCount => Samples.Length / Channels;
        private WaveAudio(float[] samples, int channels, int sampleRate)
        { Samples = samples; Channels = channels; SampleRate = sampleRate; }

        public static WaveAudio Decode(byte[] wave, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var pcm = WavResampler.ReadWave(wave);
            var width = pcm.BitsPerSample / 8;
            var samples = new float[pcm.Audio.Length / width];
            var scale = Math.Pow(2, pcm.BitsPerSample - 1);
            for (var index = 0; index < samples.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                int value;
                if (width == 1) value = pcm.Audio[index] - 128;
                else
                {
                    value = 0;
                    for (var part = 0; part < width; part++) value |= pcm.Audio[index * width + part] << (part * 8);
                    var shift = 32 - pcm.BitsPerSample;
                    value = (value << shift) >> shift;
                }
                samples[index] = (float)(value / scale);
            }
            token.ThrowIfCancellationRequested();
            return new WaveAudio(samples, pcm.Channels, pcm.SampleRate);
        }
    }
}

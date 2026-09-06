using System;
using System.Collections.Generic;

namespace ChatdollKit.SpeechPipeline
{
    /// <summary>Downmixes interleaved floats, resamples with continuous linear interpolation,
    /// and emits fixed-size mono PCM16 little-endian messages. Not thread-safe.</summary>
    public sealed class StreamingPcm16Converter
    {
        public int InputSampleRate { get; }
        public int InputChannels { get; }
        public int OutputSampleRate { get; }
        public int SamplesPerMessage { get; }
        public int BufferedSampleCount => bufferedSamples;
        private byte[] buffer;
        private int bufferedSamples;
        private bool hasPrevious;
        private double previous;
        private long nextPosition;

        public StreamingPcm16Converter(int inputSampleRate, int inputChannels, int outputSampleRate, int samplesPerMessage)
        {
            if (inputSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputSampleRate));
            if (inputChannels <= 0) throw new ArgumentOutOfRangeException(nameof(inputChannels));
            if (outputSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(outputSampleRate));
            if (samplesPerMessage <= 0 || samplesPerMessage > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(samplesPerMessage));
            InputSampleRate = inputSampleRate;
            InputChannels = inputChannels;
            OutputSampleRate = outputSampleRate;
            SamplesPerMessage = samplesPerMessage;
            buffer = new byte[samplesPerMessage * 2];
        }

        public IReadOnlyList<byte[]> Process(float[] interleaved)
        {
            if (interleaved == null) throw new ArgumentNullException(nameof(interleaved));
            if (interleaved.Length % InputChannels != 0) throw new ArgumentException("Input must contain complete interleaved frames.", nameof(interleaved));
            foreach (var sample in interleaved)
                if (float.IsNaN(sample) || float.IsInfinity(sample)) throw new ArgumentException("Audio samples must be finite.", nameof(interleaved));

            var messages = new List<byte[]>();
            for (var offset = 0; offset < interleaved.Length; offset += InputChannels)
            {
                double current = 0;
                for (var channel = 0; channel < InputChannels; channel++) current += interleaved[offset + channel];
                current /= InputChannels;
                if (!hasPrevious)
                {
                    Emit(current, messages);
                    hasPrevious = true;
                    nextPosition = InputSampleRate;
                }
                else
                {
                    while (nextPosition <= OutputSampleRate)
                    {
                        Emit(previous + (current - previous) * nextPosition / OutputSampleRate, messages);
                        nextPosition += InputSampleRate;
                    }
                    nextPosition -= OutputSampleRate;
                }
                previous = current;
            }
            return messages;
        }

        private void Emit(double sample, List<byte[]> messages)
        {
            var pcm = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(sample * 32768)));
            buffer[bufferedSamples * 2] = (byte)pcm;
            buffer[bufferedSamples * 2 + 1] = (byte)(pcm >> 8);
            if (++bufferedSamples != SamplesPerMessage) return;
            messages.Add(buffer);
            buffer = new byte[SamplesPerMessage * 2];
            bufferedSamples = 0;
        }

        /// <summary>Discards incomplete messages and interpolation history at a stream boundary.</summary>
        public void Reset()
        {
            bufferedSamples = 0;
            hasPrevious = false;
            previous = 0;
            nextPosition = 0;
        }
    }
}

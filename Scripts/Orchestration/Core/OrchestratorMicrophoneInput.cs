using System;
using System.Collections.Generic;
using System.Threading;
using ChatdollKit.SpeechPipeline;

namespace ChatdollKit.Orchestration
{
    /// <summary>Owns conversion history across microphone suppression boundaries.
    /// Convert and Reset run on the capture thread; RequestReset may run on any thread.</summary>
    public sealed class OrchestratorMicrophoneInput
    {
        private readonly StreamingPcm16Converter converter;
        public int InputChannels => converter.InputChannels;
        private int resetRequested;

        public OrchestratorMicrophoneInput(int inputSampleRate, int inputChannels, int outputSampleRate, int samplesPerMessage)
        {
            converter = new StreamingPcm16Converter(inputSampleRate, inputChannels, outputSampleRate, samplesPerMessage);
        }

        public void RequestReset() => Interlocked.Exchange(ref resetRequested, 1);

        public void Reset()
        {
            Interlocked.Exchange(ref resetRequested, 0);
            converter.Reset();
        }

        public IReadOnlyList<byte[]> Convert(float[] samples, int inputSampleRate, bool suppressed)
        {
            if (inputSampleRate != converter.InputSampleRate)
                throw new InvalidOperationException("Microphone sample rate changed. Stop and restart the orchestrator.");
            if (Interlocked.Exchange(ref resetRequested, 0) != 0 || suppressed) converter.Reset();
            return suppressed ? Array.Empty<byte[]>() : converter.Process(samples);
        }
    }
}

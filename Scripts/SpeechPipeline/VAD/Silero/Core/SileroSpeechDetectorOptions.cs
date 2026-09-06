using ChatdollKit.SpeechPipeline.VAD;
using System;

namespace ChatdollKit.SpeechPipeline.VAD.Silero
{
    [Serializable]
    public class SileroSpeechDetectorOptions : SpeechDetectorOptions
    {
        public double? VolumeDbThreshold { get; set; }
        public double SpeechProbabilityThreshold { get; set; } = 0.5;
        public int ChunkSize { get; set; } = 512;
        public bool UseVadIterator { get; set; }
        public override void Validate()
        {
            base.Validate();
            if (SampleRate != 8000 && SampleRate != 16000) throw new ArgumentOutOfRangeException(nameof(SampleRate), "Silero requires 8000 or 16000 Hz.");
            if (Channels != 1) throw new ArgumentOutOfRangeException(nameof(Channels), "Silero requires mono PCM.");
            if (ChunkSize != (SampleRate == 16000 ? 512 : 256)) throw new ArgumentOutOfRangeException(nameof(ChunkSize));
            if (!Finite(SpeechProbabilityThreshold) || SpeechProbabilityThreshold < 0 || SpeechProbabilityThreshold > 1)
                throw new ArgumentOutOfRangeException(nameof(SpeechProbabilityThreshold));
            if (VolumeDbThreshold.HasValue && !Finite(VolumeDbThreshold.Value)) throw new ArgumentOutOfRangeException(nameof(VolumeDbThreshold));
        }
    }

    [Serializable]
    public sealed class SileroStreamSpeechDetectorOptions : SileroSpeechDetectorOptions
    {
        public double SegmentSilenceThreshold { get; set; } = 0.2;
        public override void Validate()
        {
            base.Validate();
            if (!Finite(SegmentSilenceThreshold) || SegmentSilenceThreshold < 0) throw new ArgumentOutOfRangeException(nameof(SegmentSilenceThreshold));
        }
    }
}

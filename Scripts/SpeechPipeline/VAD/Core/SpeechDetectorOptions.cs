using System;

namespace ChatdollKit.SpeechPipeline.VAD
{
    [Serializable]
    public class SpeechDetectorOptions
    {
        public int SampleRate { get; set; } = 16000;
        public int Channels { get; set; } = 1;
        public double SilenceDurationThreshold { get; set; } = 0.5;
        public double MaxDuration { get; set; } = 10.0;
        public double MinDuration { get; set; } = 0.2;
        public int PrerollBufferCount { get; set; } = 5;
        public double RecordingStartedMinDuration { get; set; } = 1.5;
        public int RecordingStartedMinTextLength { get; set; } = 2;
        public SpeechDetectorOptions Copy() => (SpeechDetectorOptions)MemberwiseClone();
        public virtual void Validate()
        {
            if (SampleRate <= 0 || Channels <= 0) throw new ArgumentOutOfRangeException(nameof(SampleRate));
            if (!Finite(SilenceDurationThreshold) || SilenceDurationThreshold < 0 ||
                !Finite(MaxDuration) || MaxDuration <= 0 || !Finite(MinDuration) || MinDuration < 0 ||
                !Finite(RecordingStartedMinDuration) || RecordingStartedMinDuration < 0 ||
                PrerollBufferCount < 0 || RecordingStartedMinTextLength < 0)
                throw new ArgumentOutOfRangeException(nameof(SpeechDetectorOptions));
        }
        protected static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    [Serializable]
    public sealed class StandardSpeechDetectorOptions : SpeechDetectorOptions
    {
        public double VolumeDbThreshold { get; set; } = -40.0;
        public override void Validate()
        {
            base.Validate();
            if (!Finite(VolumeDbThreshold)) throw new ArgumentOutOfRangeException(nameof(VolumeDbThreshold));
        }
    }


}

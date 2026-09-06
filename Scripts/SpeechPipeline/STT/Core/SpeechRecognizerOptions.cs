using System;

namespace ChatdollKit.SpeechPipeline.STT
{
    public class SpeechRecognizerOptions
    {
        public string Language { get; set; }
        public string[] AlternativeLanguages { get; set; } = Array.Empty<string>();
        public int SampleRate { get; set; } = 16000;
        public double TimeoutSeconds { get; set; } = 10;
        /// <summary>Total attempts, including the initial request.</summary>
        public int MaxAttempts { get; set; } = 2;

        /// <summary>Provider options should additionally copy any mutable fields they introduce.</summary>
        public virtual SpeechRecognizerOptions Copy()
        {
            var copy = (SpeechRecognizerOptions)MemberwiseClone();
            copy.AlternativeLanguages = AlternativeLanguages == null
                ? Array.Empty<string>() : (string[])AlternativeLanguages.Clone();
            return copy;
        }

        public virtual void Validate()
        {
            if (SampleRate <= 0 || SampleRate > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(SampleRate));
            if (double.IsNaN(TimeoutSeconds) || double.IsInfinity(TimeoutSeconds) || TimeoutSeconds <= 0 ||
                TimeoutSeconds > int.MaxValue / 1000.0)
                throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
            if (MaxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        }
    }
}

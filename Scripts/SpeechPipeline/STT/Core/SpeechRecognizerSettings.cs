using System;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.STT
{
    [Serializable]
    public sealed class SpeechRecognizerSettings
    {
        public string Language = "ja";
        public string[] AlternativeLanguages = Array.Empty<string>();
        [Min(1)] public int SampleRate = 16000;
        [Min(0.01f)] public float TimeoutSeconds = 10;
        [Min(1)] public int MaxAttempts = 2;

        public void ApplyTo(SpeechRecognizerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Language = Language;
            options.AlternativeLanguages = AlternativeLanguages == null
                ? Array.Empty<string>() : (string[])AlternativeLanguages.Clone();
            options.SampleRate = SampleRate;
            options.TimeoutSeconds = TimeoutSeconds;
            options.MaxAttempts = MaxAttempts;
        }
    }
}

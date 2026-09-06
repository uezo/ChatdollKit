using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.TTS
{
    [Serializable]
    public struct SpeechSynthesisMapping
    {
        public string Key;
        public string Value;

        public static Dictionary<string, string> ToDictionary(SpeechSynthesisMapping[] entries)
        {
            var result = new Dictionary<string, string>();
            foreach (var entry in entries ?? Array.Empty<SpeechSynthesisMapping>())
            {
                if (string.IsNullOrEmpty(entry.Key) || entry.Value == null)
                    throw new ArgumentException("Speech synthesis mappings require a key and value.", nameof(entries));
                if (result.ContainsKey(entry.Key))
                    throw new ArgumentException("Speech synthesis mappings require unique keys.", nameof(entries));
                result.Add(entry.Key, entry.Value);
            }
            return result;
        }
    }

    /// <summary>Inspector settings applied to an options copy without replacing processors registered in code.</summary>
    [Serializable]
    public sealed class SpeechSynthesizerSettings
    {
        [Tooltip("Resample WAV output to Sample Rate. Leave disabled to retain the provider's output rate.")]
        public bool UseSampleRate;
        [Min(1)] public int SampleRate = 16000;
        [Min(0.01f)] public float TimeoutSeconds = 10;
        public string CacheDirectory;
        public string CacheExtension = "wav";
        public SpeechSynthesisMapping[] StyleMapper = Array.Empty<SpeechSynthesisMapping>();

        public void ApplyTo(SpeechSynthesizerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.SampleRate = UseSampleRate ? (int?)SampleRate : null;
            options.TimeoutSeconds = TimeoutSeconds;
            options.CacheDirectory = CacheDirectory;
            options.CacheExtension = CacheExtension;
            options.StyleMapper = SpeechSynthesisMapping.ToDictionary(StyleMapper);
        }
    }
}

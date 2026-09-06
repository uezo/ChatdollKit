using System;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.TTS
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/TTS/Google Speech Synthesizer")]
    public sealed class GoogleSpeechSynthesizer : SpeechSynthesizerComponent
    {
        public string ApiKey;
        public string Speaker = "ja-JP-Standard-A";
        public string DefaultLanguage = "ja-JP";
        public string AudioFormat = "LINEAR16";
        [Tooltip("Optional language-to-speaker overrides.")]
        public SpeechSynthesisMapping[] VoiceMap = Array.Empty<SpeechSynthesisMapping>();
        public SpeechSynthesizerSettings Settings = new SpeechSynthesizerSettings { CacheExtension = "pcm" };

        public override SpeechSynthesizerOptions BuildOptions(SpeechSynthesizerOptions current = null)
        {
            var options = (GoogleSpeechSynthesizerOptions)(current?.Copy() ?? new GoogleSpeechSynthesizerOptions());
            Settings.ApplyTo(options);
            options.ApiKey = ApiKey;
            options.Speaker = Speaker;
            options.DefaultLanguage = DefaultLanguage;
            options.AudioFormat = AudioFormat;
            options.VoiceMap = SpeechSynthesisMapping.ToDictionary(VoiceMap);
            return options;
        }

        public override ISpeechSynthesizer CreateSynthesizer(SpeechSynthesizerOptions options)
            => new GoogleSpeechSynthesizerClient((GoogleSpeechSynthesizerOptions)options);
    }
}

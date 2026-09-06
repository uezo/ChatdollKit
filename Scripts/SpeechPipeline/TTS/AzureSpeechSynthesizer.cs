using System;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.TTS
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/TTS/Azure Speech Synthesizer")]
    public sealed class AzureSpeechSynthesizer : SpeechSynthesizerComponent
    {
        public string ApiKey;
        public string Region = "japaneast";
        public string Speaker = "ja-JP-AoiNeural";
        public string DefaultLanguage = "ja-JP";
        public string AudioFormat = "riff-16khz-16bit-mono-pcm";
        [Tooltip("Optional language-to-speaker overrides.")]
        public SpeechSynthesisMapping[] VoiceMap = Array.Empty<SpeechSynthesisMapping>();
        public SpeechSynthesizerSettings Settings = new SpeechSynthesizerSettings();

        public override SpeechSynthesizerOptions BuildOptions(SpeechSynthesizerOptions current = null)
        {
            var options = (AzureSpeechSynthesizerOptions)(current?.Copy() ?? new AzureSpeechSynthesizerOptions());
            Settings.ApplyTo(options);
            options.ApiKey = ApiKey;
            options.Region = Region;
            options.Speaker = Speaker;
            options.DefaultLanguage = DefaultLanguage;
            options.AudioFormat = AudioFormat;
            options.VoiceMap = SpeechSynthesisMapping.ToDictionary(VoiceMap);
            return options;
        }

        public override ISpeechSynthesizer CreateSynthesizer(SpeechSynthesizerOptions options)
            => new AzureSpeechSynthesizerClient((AzureSpeechSynthesizerOptions)options);
    }
}

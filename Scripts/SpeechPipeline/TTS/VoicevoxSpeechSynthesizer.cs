using UnityEngine;

namespace ChatdollKit.SpeechPipeline.TTS
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/TTS/VOICEVOX Speech Synthesizer")]
    public sealed class VoicevoxSpeechSynthesizer : SpeechSynthesizerComponent
    {
        public string BaseUrl = "http://127.0.0.1:50021";
        [Min(0)] public int Speaker = 46;
        public SpeechSynthesizerSettings Settings = new SpeechSynthesizerSettings();

        public override SpeechSynthesizerOptions BuildOptions(SpeechSynthesizerOptions current = null)
        {
            var options = (VoicevoxSpeechSynthesizerOptions)(current?.Copy() ?? new VoicevoxSpeechSynthesizerOptions());
            Settings.ApplyTo(options);
            options.BaseUrl = BaseUrl;
            options.Speaker = Speaker;
            return options;
        }

        public override ISpeechSynthesizer CreateSynthesizer(SpeechSynthesizerOptions options)
            => new VoicevoxSpeechSynthesizerClient((VoicevoxSpeechSynthesizerOptions)options);
    }
}

using UnityEngine;

namespace ChatdollKit.SpeechPipeline.TTS
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/TTS/OpenAI Speech Synthesizer")]
    public sealed class OpenAISpeechSynthesizer : SpeechSynthesizerComponent
    {
        public string ApiKey;
        public string BaseUrl = "https://api.openai.com/v1";
        // Retained only so the editor can migrate existing serialized settings.

        public string Speaker = "sage";
        public string Model = "tts-1";
        [TextArea(2, 6)] public string Instructions;
        public string AudioFormat = "wav";
        public SpeechSynthesizerSettings Settings = new SpeechSynthesizerSettings();

        public override SpeechSynthesizerOptions BuildOptions(SpeechSynthesizerOptions current = null)
        {
            var options = (OpenAISpeechSynthesizerOptions)(current?.Copy() ?? new OpenAISpeechSynthesizerOptions());
            Settings.ApplyTo(options);
            options.ApiKey = ApiKey;
            options.BaseUrl = BaseUrl;
            options.Speaker = Speaker;
            options.Model = Model;
            options.Instructions = string.IsNullOrEmpty(Instructions) ? null : Instructions;
            options.AudioFormat = AudioFormat;
            return options;
        }

        public override ISpeechSynthesizer CreateSynthesizer(SpeechSynthesizerOptions options)
            => new OpenAISpeechSynthesizerClient((OpenAISpeechSynthesizerOptions)options);
    }
}

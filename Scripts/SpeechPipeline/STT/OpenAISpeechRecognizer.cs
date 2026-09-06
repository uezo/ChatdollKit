using System.Globalization;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.STT
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/STT/OpenAI Speech Recognizer")]
    public sealed class OpenAISpeechRecognizer : SpeechRecognizerComponent
    {
        public string ApiKey;
        public string BaseUrl = "https://api.openai.com/v1";
        // Retained only so the editor can migrate existing serialized settings.

        public SpeechRecognizerSettings Settings = new SpeechRecognizerSettings();
        public string Model = "gpt-transcribe";
        [Min(0)] public int MinDataLength = 4096;

        public override string GetRestartKey() => Settings.SampleRate.ToString(CultureInfo.InvariantCulture);

        public override SpeechRecognizerOptions BuildOptions(SpeechRecognizerOptions current = null)
        {
            var options = (OpenAISpeechRecognizerOptions)(current?.Copy() ?? new OpenAISpeechRecognizerOptions());
            Settings.ApplyTo(options);
            options.ApiKey = ApiKey;
            options.BaseUrl = BaseUrl;
            options.Model = Model;
            options.MinDataLength = MinDataLength;
            return options;
        }

        public override ISpeechRecognizer CreateRecognizer(SpeechRecognizerOptions options)
            => new OpenAISpeechRecognizerClient((OpenAISpeechRecognizerOptions)options);
    }
}

using System.Globalization;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.STT
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/STT/Azure Speech Recognizer")]
    public sealed class AzureSpeechRecognizer : SpeechRecognizerComponent
    {
        public string ApiKey;
        public string Region = "japaneast";
        public string ApiVersion = "2024-11-15";
        public SpeechRecognizerSettings Settings = new SpeechRecognizerSettings
        {
            Language = "ja-JP",
            TimeoutSeconds = 5
        };

        public override string GetRestartKey() => Settings.SampleRate.ToString(CultureInfo.InvariantCulture);

        public override SpeechRecognizerOptions BuildOptions(SpeechRecognizerOptions current = null)
        {
            var options = (AzureSpeechRecognizerOptions)(current?.Copy() ?? new AzureSpeechRecognizerOptions());
            Settings.ApplyTo(options);
            options.ApiKey = ApiKey;
            options.Region = Region;
            options.ApiVersion = ApiVersion;
            return options;
        }

        public override ISpeechRecognizer CreateRecognizer(SpeechRecognizerOptions options)
            => new AzureSpeechRecognizerClient((AzureSpeechRecognizerOptions)options);
    }
}

using System;

namespace ChatdollKit.Tests.SpeechPipeline.LiveApi
{
    public static partial class TtsApiTestSettings
    {
        public static string OpenAIApiKey { get; private set; }
        public static string OpenAIModel { get; private set; } = "gpt-4o-mini-tts";
        public static string OpenAISpeaker { get; private set; } = "sage";
        public static string AzureApiKey { get; private set; }
        public static string AzureRegion { get; private set; }
        public static string AzureSpeaker { get; private set; } = "ja-JP-NanamiNeural";
        public static string GoogleApiKey { get; private set; }
        public static string GoogleSpeaker { get; private set; } = "ja-JP-Neural2-B";
        public static string VoicevoxBaseUrl { get; private set; }
        public static int VoicevoxSpeaker { get; private set; } = 46;

        static TtsApiTestSettings()
        {
            ConfigureLocal();
            OpenAIApiKey = First(OpenAIApiKey, SpeechApiTestSettings.OpenAIApiKey);
            AzureApiKey = First(AzureApiKey, SpeechApiTestSettings.AzureApiKey);
            AzureRegion = First(AzureRegion, SpeechApiTestSettings.AzureRegion);
            GoogleApiKey = First(GoogleApiKey, Environment.GetEnvironmentVariable("GOOGLE_TTS_API_KEY"));
            VoicevoxBaseUrl = First(VoicevoxBaseUrl, Environment.GetEnvironmentVariable("VOICEVOX_BASE_URL"));
        }
        static partial void ConfigureLocal();
        private static string First(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

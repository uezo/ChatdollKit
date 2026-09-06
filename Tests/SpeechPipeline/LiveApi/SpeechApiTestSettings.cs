using System;

namespace ChatdollKit.Tests.SpeechPipeline.LiveApi
{
    /// <summary>Optional live-test settings. Put local values in the ignored partial
    /// file SpeechApiTestSettings.Local.cs, or use the documented environment variables.</summary>
    public static partial class SpeechApiTestSettings
    {
        public static string OpenAIApiKey { get; private set; }
        public static string AzureApiKey { get; private set; }
        public static string AzureRegion { get; private set; }
        public static string AudioFilePath { get; private set; }
        public static string ExpectedTextContains { get; private set; }

        static SpeechApiTestSettings()
        {
            ConfigureLocal();
            OpenAIApiKey = WithFallback(OpenAIApiKey, "OPENAI_API_KEY");
            AzureApiKey = WithFallback(AzureApiKey, "AZURE_SPEECH_API_KEY");
            AzureRegion = WithFallback(AzureRegion, "AZURE_SPEECH_REGION");
            AudioFilePath = WithFallback(AudioFilePath, "CHATDOLLKIT_SPEECH_TEST_AUDIO_FILE");
            ExpectedTextContains = WithFallback(ExpectedTextContains, "CHATDOLLKIT_SPEECH_TEST_EXPECTED_TEXT");
        }

        static partial void ConfigureLocal();

        private static string WithFallback(string localValue, string environmentVariable)
            => string.IsNullOrWhiteSpace(localValue) ? Environment.GetEnvironmentVariable(environmentVariable) : localValue;
    }
}

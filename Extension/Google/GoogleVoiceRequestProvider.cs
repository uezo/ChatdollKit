using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Google
{
    public class GoogleVoiceRequestProvider : VoiceRequestProviderBase
    {
        [Header("Google Cloud Settings")]
        public string ApiKey = string.Empty;
        public string Language = "ja-JP";
        public bool UseEnhancedModel = false;
        public List<SpeechContext> SpeechContexts;

        public void Configure(string apiKey, string language, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            Language = string.IsNullOrEmpty(Language) || overwrite ? language : Language;
        }

        protected override async UniTask<string> RecognizeSpeechAsync(VoiceRecorderResponse recordedVoice)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key or Language are missing from GoogleVoiceRequestProvider");
            }

            var response = await client.PostJsonAsync<SpeechRecognitionResponse>(
                $"https://speech.googleapis.com/v1/speech:recognize?key={ApiKey}",
                new SpeechRecognitionRequest(recordedVoice.Voice, Language, UseEnhancedModel, SpeechContexts, recordedVoice.SamplingData));

            return response?.results?[0]?.alternatives?[0]?.transcript ?? string.Empty;
        }
    }
}

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Azure
{
    public class AzureVoiceRequestProvider : VoiceRequestProviderBase
    {
        [Header("Azure Settings")]
        public string ApiKey = string.Empty;
        public string Region = string.Empty;
        public string Language = "ja-JP";

        public void Configure(string apiKey, string language, string region, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            Language = string.IsNullOrEmpty(Language) || overwrite ? language : Language;
            Region = string.IsNullOrEmpty(Region) || overwrite ? region : Region;
        }

        protected override async UniTask<string> RecognizeSpeechAsync(VoiceRecorderResponse recordedVoice)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Region) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key, Region and Language are missing from AzureVoiceRequestProvider");
            }

            var headers = new Dictionary<string, string>()
            {
                { "Ocp-Apim-Subscription-Key", ApiKey }
            };

            // TODO: Sending chunk is the better way
            // https://docs.microsoft.com/ja-jp/azure/cognitive-services/speech-service/rest-speech-to-text#chunked-transfer
            var response = await client.PostBytesAsync<SpeechRecognitionResponse>(
                $"https://{Region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={Language}",
                AudioConverter.AudioClipToPCM(recordedVoice.Voice, recordedVoice.SamplingData),
                headers);

            return response?.DisplayText ?? string.Empty;
        }

        // Response from Azure STT
        class SpeechRecognitionResponse
        {
            public string RecognitionStatus;
            public string DisplayText;
            public int Offset;
            public int Duration;
        }
    }
}

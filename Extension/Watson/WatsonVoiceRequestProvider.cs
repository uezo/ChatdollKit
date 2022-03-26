using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Watson
{
    public class WatsonVoiceRequestProvider : VoiceRequestProviderBase
    {
        [Header("Watson Settings")]
        public string ApiKey = string.Empty;
        public string Model = "ja-JP_BroadbandModel";
        public string BaseUrl = string.Empty;
        public bool RemoveWordSeparation = true;

        public void Configure(string apiKey, string model, string baseUrl, bool removeWordSeparation = true, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            Model = string.IsNullOrEmpty(Model) || overwrite ? model : Model;
            BaseUrl = string.IsNullOrEmpty(BaseUrl) || overwrite ? baseUrl : BaseUrl;
            RemoveWordSeparation = overwrite ? removeWordSeparation : RemoveWordSeparation;
        }

        protected override async UniTask<string> RecognizeSpeechAsync(VoiceRecorderResponse recordedVoice)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Model) || string.IsNullOrEmpty(BaseUrl))
            {
                Debug.LogError("API Key, Model or Language are missing from WatsonVoiceRequestProvider");
            }

            var headers = new Dictionary<string, string>()
            {
                { "Authorization", client.GetBasicAuthenticationHeaderValue("apikey", ApiKey).ToString() }
            };

            // TODO: Sending as streaming is the better way
            // TODO: Accept more parameters
            var response = await client.PostBytesAsync<SpeechRecognitionResponse>(
                $"{BaseUrl}/v1/recognize?model={Model}",
                AudioConverter.AudioClipToPCM(recordedVoice.Voice, recordedVoice.SamplingData),
                headers);

            var recognizedText = response?.results?[0]?.alternatives?[0]?.transcript ?? string.Empty;
            if (RemoveWordSeparation)
            {
                recognizedText = recognizedText.Replace(" ", "");
            }
            return recognizedText;
        }

        // Models for response
        public class SpeechRecognitionResponse
        {
            public List<SpeechRecognitionResult> results { get; set; }
        }

        public class SpeechRecognitionResult
        {
            public List<SpeechRecognitionAlternative> alternatives { get; set; }
        }

        public class SpeechRecognitionAlternative
        {
            public double confidence { get; set; }
            public string word { get; set; }
            public string transcript { get; set; }
        }
    }
}

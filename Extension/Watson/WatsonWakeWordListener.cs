using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Watson
{
    public class WatsonWakeWordListener : WakeWordListenerBase
    {
        [Header("Watson Settings")]
        public string ApiKey = string.Empty;
        public string Model = "ja-JP_BroadbandModel";
        public string BaseUrl = string.Empty;
        public bool RemoveWordSeparation = true;

        protected override async Task<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Model) || string.IsNullOrEmpty(BaseUrl))
            {
                Debug.LogError("API Key, Model or Language are missing from WatsonWakeWordListener");
            }

            var headers = new Dictionary<string, string>()
            {
                { "Authorization", client.GetBasicAuthenticationHeaderValue("apikey", ApiKey).ToString() }
            };

            // TODO: Sending as streaming is the better way
            // TODO: Accept more parameters
            var response = await client.PostBytesAsync<SpeechRecognitionResponse>(
                $"{BaseUrl}/v1/recognize?model={Model}",
                AudioConverter.AudioClipToPCM(recordedVoice),
                headers);

            var recognizedText = response?.results?[0]?.alternatives?[0]?.transcript ?? string.Empty;
            if (RemoveWordSeparation)
            {
                recognizedText = recognizedText.Replace(" ", "");
            }
            return recognizedText;
        }

#pragma warning disable CS0649
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

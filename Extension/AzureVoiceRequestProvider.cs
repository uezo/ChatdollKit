using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.IO;


namespace ChatdollKit.Extension
{
    public class AzureVoiceRequestProvider : VoiceRequestProviderBase
    {
        [Header("Azure Settings")]
        public string ApiKey = string.Empty;
        public string Region = string.Empty;
        public string Language = "ja-JP";

        protected override async Task<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            var headers = new Dictionary<string, string>()
            {
                { "Ocp-Apim-Subscription-Key", ApiKey }
            };

            // TODO: Sending chunk is the better way
            // https://docs.microsoft.com/ja-jp/azure/cognitive-services/speech-service/rest-speech-to-text#chunked-transfer
            var response = await client.PostBytesAsync<SpeechRecognitionResponse>(
                $"https://{Region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={Language}",
                AudioConverter.AudioClipToPCM(recordedVoice),
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

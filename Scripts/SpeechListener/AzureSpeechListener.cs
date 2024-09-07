using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechListener
{
    public class AzureSpeechListener : SpeechListenerBase
    {
        [Header("Azure Settings")]
        public string ApiKey = string.Empty;
        public string Region = string.Empty;
        public string Language = "ja-JP";

        protected override async UniTask<string> ProcessTranscriptionAsync(float[] samples, CancellationToken token)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Region) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key, Region and Language are missing for AzureSpeechListener");
            }

            var url = $"https://{Region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={Language}";
            var requestData = SampleToPCM(samples, microphoneManager.SampleRate, 1);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(requestData);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Ocp-Apim-Subscription-Key", ApiKey);

                try
                {
                    await request.SendWebRequest().ToUniTask();
                    var response = JsonConvert.DeserializeObject<SpeechRecognitionResponse>(request.downloadHandler.text);
                    return response?.DisplayText ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error at ProcessTranscriptionAsync: {ex.Message}\n{ex.StackTrace}");
                    throw ex;
                }
            }
        }

        class SpeechRecognitionResponse
        {
            public string RecognitionStatus;
            public string DisplayText;
            public int Offset;
            public int Duration;
        }
    }   
}

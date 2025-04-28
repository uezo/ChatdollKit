using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

namespace ChatdollKit.SpeechListener
{
    public class AzureSpeechListener : SpeechListenerBase
    {
        [Header("Azure Settings")]
        public string ApiKey = string.Empty;
        public string Region = string.Empty;
        public bool UseClassic = false;

        protected override async UniTask<string> ProcessTranscriptionAsync(float[] samples, int sampleRate, CancellationToken token)
        {
            if (UseClassic)
            {
                return await ProcessTranscriptionClassicAsync(samples, sampleRate, token);
            }
            else
            {
                return await ProcessTranscriptionFastAsync(samples, sampleRate, token);
            }
        }

        protected async UniTask<string> ProcessTranscriptionClassicAsync(float[] samples, int sampleRate, CancellationToken token)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Region) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key, Region and Language are missing for AzureSpeechListener");
            }

            var url = $"https://{Region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={Language}";
            var requestData = SampleToPCM(samples, sampleRate, 1);

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

        protected async UniTask<string> ProcessTranscriptionFastAsync(float[] samples, int sampleRate, CancellationToken token)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Region) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key, Region and Language are missing for AzureSpeechListener");
            }

            var url = $"https://{Region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15";

            var locales = new List<string>(){ Language };
            if (AlternativeLanguages != null)
            {
                locales.AddRange(AlternativeLanguages);
            }

            var form = new WWWForm();
            form.AddField("definition", JsonConvert.SerializeObject(new Dictionary<string, object>(){
                {"locales", locales},
                {"channels", new List<int>(){0, 1}}
            }));
            form.AddBinaryData("audio", SampleToPCM(samples, sampleRate, 1), "voice.wav");

            using (UnityWebRequest request = UnityWebRequest.Post(url, form))
            {
                request.SetRequestHeader("Ocp-Apim-Subscription-Key", ApiKey);

                try
                {
                    await request.SendWebRequest().ToUniTask();
                    var response = JsonConvert.DeserializeObject<FastSpeechRecognitionResponse>(request.downloadHandler.text);
                    return response.combinedPhrases[0].text;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error at SendWebRequest() to POST {url}: {ex.Message}\n{ex.StackTrace}");
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

        class CombinedPhrase
        {
            public string text;
        }

        class FastSpeechRecognitionResponse
        {
            public List<CombinedPhrase> combinedPhrases;
        }
    }   
}

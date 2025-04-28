using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechListener
{
    public class GoogleSpeechListener : SpeechListenerBase
    {
        [Header("Google Cloud Settings")]
        public string ApiKey = string.Empty;
        public bool UseEnhancedModel = false;
        public List<SpeechContext> SpeechContexts;

        protected override async UniTask<string> ProcessTranscriptionAsync(float[] samples, int sampleRate, CancellationToken token)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key or Language are missing for GoogleSpeechListener");
            }

            var url = $"https://speech.googleapis.com/v1/speech:recognize?key={ApiKey}";
            var requestData = new SpeechRecognitionRequest(
                sampleRate, 1, Language, UseEnhancedModel, SpeechContexts, samples
            );
            if (AlternativeLanguages?.Count > 0)
            {
                requestData.config.alternativeLanguageCodes = AlternativeLanguages;
            }
            var json = JsonConvert.SerializeObject(requestData);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                try
                {
                    await request.SendWebRequest().ToUniTask();
                    var response = JsonConvert.DeserializeObject<SpeechRecognitionResponse>(request.downloadHandler.text);
                    return response?.results?[0]?.alternatives?[0]?.transcript ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error at ProcessTranscriptionAsync: {ex.Message}\n{ex.StackTrace}");
                    throw ex;
                }
            }
        }

        public static string AudioClipToBase64(float[] samplingData, int frequency, int channels)
        {
            return Convert.ToBase64String(SampleToPCM(samplingData, frequency, channels));
        }

        private class SpeechRecognitionRequest
        {
            public SpeechRecognitionConfig config;
            public SpeechRecognitionAudio audio;

            public SpeechRecognitionRequest(int frequency, int channels, string languageCode, bool useEnhancedModel, List<SpeechContext> speechContexts, float[] samplingData = null)
            {
                config = new SpeechRecognitionConfig(frequency, channels, languageCode, useEnhancedModel, speechContexts);
                audio = new SpeechRecognitionAudio(Convert.ToBase64String(SampleToPCM(samplingData, frequency, channels)));
            }
        }

        private class SpeechRecognitionConfig
        {
            public int encoding;
            public double sampleRateHertz;
            public double audioChannelCount;
            public bool enableSeparateRecognitionPerChannel;
            public string languageCode;
            public List<string> alternativeLanguageCodes;
            public string model;
            public bool useEnhanced;
            public List<SpeechContext> speechContexts;

            public SpeechRecognitionConfig(int frequency, int channels, string languageCode, bool useEnhancedModel, List<SpeechContext> speechContexts)
            {
                encoding = 1;   // 1: 16-bit linear PCM
                sampleRateHertz = frequency;
                audioChannelCount = channels;
                enableSeparateRecognitionPerChannel = false;
                this.languageCode = languageCode;
                model = useEnhancedModel ? null : "default";
                useEnhanced = useEnhancedModel;
                this.speechContexts = speechContexts;
            }
        }

        private class SpeechRecognitionAudio
        {
            public string content;

            public SpeechRecognitionAudio(string content)
            {
                this.content = content;
            }
        }

        [Serializable]
        public class SpeechContext
        {
            public List<string> phrases;
            public int boost;
        }

        private class SpeechRecognitionResponse
        {
            public SpeechRecognitionResult[] results;
        }

        private class SpeechRecognitionResult
        {
            public SpeechRecognitionAlternative[] alternatives;
        }

        private class SpeechRecognitionAlternative
        {
            public string transcript;
            public double confidence;
        }
    }   
}

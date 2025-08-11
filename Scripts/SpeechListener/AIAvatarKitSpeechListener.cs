using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechListener
{
    public class AIAvatarKitSpeechListener : SpeechListenerBase
    {
        [Header("Azure Settings")]
        public string EndpointUrl;
        public string ApiKey = string.Empty;

        protected override async UniTask<string> ProcessTranscriptionAsync(float[] samples, int sampleRate, CancellationToken token)
        {
            var form = new WWWForm();
            form.AddBinaryData("audio", SampleToPCM(samples, sampleRate, 1), "voice.wav");

            using (UnityWebRequest request = UnityWebRequest.Post(EndpointUrl, form))
            {
                request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");

                try
                {
                    await request.SendWebRequest().ToUniTask();
                    var response = JsonConvert.DeserializeObject<SpeechRecognitionResponse>(request.downloadHandler.text);
                    return response.text;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error at SendWebRequest() to POST {EndpointUrl}: {ex.Message}\n{ex.StackTrace}");
                    throw ex;
                }
            }
        }

        class SpeechRecognitionResponse
        {
            public string text;
        }
    }   
}

using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

namespace ChatdollKit.SpeechListener
{
    public class AIAvatarKitSpeechListener : SpeechListenerBase
    {
        [Header("Azure Settings")]
        public string EndpointUrl;
        public string ApiKey = string.Empty;
        public Func<SpeechRecognitionResponse, string> PostProcess;

        protected override async UniTask<string> ProcessTranscriptionAsync(float[] samples, int sampleRate, CancellationToken token)
        {
            var form = new WWWForm();
            form.AddBinaryData("audio", SampleToPCM(samples, sampleRate, 1), "voice.wav");

            using (UnityWebRequest request = UnityWebRequest.Post(EndpointUrl, form))
            {
                if (!string.IsNullOrEmpty(ApiKey))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");
                }

                try
                {
                    await request.SendWebRequest().ToUniTask();
                    var response = JsonConvert.DeserializeObject<SpeechRecognitionResponse>(request.downloadHandler.text);
                    if (PostProcess != null)
                    {
                        return PostProcess(response);
                    }
                    else
                    {
                        return response.text;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error at SendWebRequest() to POST {EndpointUrl}: {ex.Message}\n{ex.StackTrace}");
                    throw ex;
                }
            }
        }

        public class Candidate
        {
            public string speaker_id;
            public float similarity;
            public Dictionary<string, object> metadata;
            public bool is_new = false;
        }

        public class MatchTopKResult
        {
            public Candidate chosen;
            public List<Candidate> candidates;
        }

        public class SpeechRecognitionResponse
        {
            public string text;
            public Dictionary<string, object> preprocess_metadata;
            public Dictionary<string, object> postprocess_metadata;
            public MatchTopKResult speakers;
        }
    }   
}

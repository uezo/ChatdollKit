using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechListener
{
    public class OpenAISpeechListener : SpeechListenerBase
    {
        [Header("OpenAI Settings")]
        public string ApiKey;
        public string Model = "whisper-1";
        public string Language;
        public string Prompt;
        public float Temperature = 0.0f;

        // See API document: https://platform.openai.com/docs/api-reference/audio/createTranscription
        protected override async UniTask<string> ProcessTranscriptionAsync(float[] samples, CancellationToken token)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Model))
            {
                Debug.LogError("API Key and/or Model are missing for OpenAISpeechListener");
            }

            var form = new WWWForm();
            form.AddField("model", Model);
            if (!string.IsNullOrEmpty(Language))
            {
                form.AddField("language", Language);
            }
            form.AddField("response_format", "text");
            form.AddBinaryData("file", SampleToPCM(samples, microphoneManager.SampleRate, 1), "voice.wav"); // filename is required to transcribe
            if (!string.IsNullOrEmpty(Prompt))
            {
                form.AddField("prompt", Prompt);
            }
            form.AddField("temperature", Temperature.ToString());

            using (UnityWebRequest request = UnityWebRequest.Post("https://api.openai.com/v1/audio/transcriptions", form))
            {
                request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");

                try
                {
                    await request.SendWebRequest().ToUniTask();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error at SendWebRequest() to POST https://api.openai.com/v1/audio/transcriptions: {ex.Message}\n{ex.StackTrace}");
                    throw ex;
                }

                return request.downloadHandler.text;
            }
        }
    }   
}

using System;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.OpenAI
{
    public class OpenAIVoiceRequestProvider : VoiceRequestProviderBase
    {
        [Header("OpenAI Settings")]
        public string ApiKey;
        public string Model = "whisper-1";
        public string Language;
        public string Prompt;
        public float Temperature = 0.0f;

        public void Configure(string apiKey, string language, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            Language = string.IsNullOrEmpty(Language) || overwrite ? language : Language;
        }

        // See API document: https://platform.openai.com/docs/api-reference/audio/createTranscription
        protected override async UniTask<string> RecognizeSpeechAsync(VoiceRecorderResponse recordedVoice)
        {
            var form = new WWWForm();
            form.AddField("model", "whisper-1");
            if (!string.IsNullOrEmpty(Language))
            {
                form.AddField("language", Language);
            }
            form.AddField("response_format", "text");
            form.AddBinaryData("file", AudioConverter.AudioClipToPCM(recordedVoice.Voice, recordedVoice.SamplingData), "voice.wav"); // filename is required to transcribe
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

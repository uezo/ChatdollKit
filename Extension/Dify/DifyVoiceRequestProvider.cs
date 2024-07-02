using System;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Dify
{
    public class DifyVoiceRequestProvider : VoiceRequestProviderBase
    {
        [Header("Dify Settings")]
        public string ApiKey;
        public string BaseUrl;

        protected override async UniTask<string> RecognizeSpeechAsync(VoiceRecorderResponse recordedVoice)
        {
            var form = new WWWForm();
            form.AddBinaryData("file", AudioConverter.AudioClipToPCM(recordedVoice.Voice, recordedVoice.SamplingData), "voice.wav", "audio/wav");

            using (UnityWebRequest request = UnityWebRequest.Post(BaseUrl + "/audio-to-text", form))
            {
                request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");

                try
                {
                    await request.SendWebRequest().ToUniTask();
                    return JsonConvert.DeserializeObject<DifySTTResponse>(request.downloadHandler.text).text;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error at RecognizeSpeechAsync: {ex.Message}\n{ex.StackTrace}");
                    throw ex;
                }
            }
        }

        protected class DifySTTResponse
        {
            public string text { get; set; }
        }
    }
}

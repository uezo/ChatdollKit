using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Model;

namespace ChatdollKit.Extension.Dify
{
    public class DifyTTSLoader : WebVoiceLoaderBase
    {
        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "Dify";
        public override string Name
        {
            get
            {
                return _Name;
            }
        }
        public bool _IsDefault = true;
        public override bool IsDefault
        {
            get
            {
                return _IsDefault;
            }
            set
            {
                _IsDefault = value;
            }
        }

        [Header("Dify Settings")]
        public string ApiKey;
        public string BaseUrl;
        public string User;
        public AudioType AudioType = AudioType.MPEG;

        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            if (string.IsNullOrEmpty(voice.Text.Replace(" ", "").Replace("\n", "").Trim()))
            {
                Debug.LogWarning("Query for Dify TTS is empty");
                return null;
            }

            var headers = new Dictionary<string, string>()
            {
                { "Authorization", $"Bearer {ApiKey}"  },
                { "Content-Type", "application/json" }
            };

            var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Dictionary<string, object>() {
                { "text", voice.Text },
                { "user", User },
                { "streaming", false }
            }));

            return await DownloadAudioClipNativeAsync(BaseUrl + "/text-to-audio", data, headers, token);
        }

        protected async UniTask<AudioClip> DownloadAudioClipNativeAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType))
            {
                www.timeout = Timeout;
                www.method = "POST";

                www.SetRequestHeader("Authorization", headers["Authorization"]);
                www.SetRequestHeader("Content-Type", headers["Content-Type"]);

                www.uploadHandler = new UploadHandlerRaw(data);

                try
                {
                    await www.SendWebRequest().ToUniTask(cancellationToken: token);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occured while processing Dify text-to-speech: {ex}");
                    return null;
                }
                return DownloadHandlerAudioClip.GetContent(www);
            }
        }
    }
}

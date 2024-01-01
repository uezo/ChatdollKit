using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Model;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.OpenAI
{
    public class OpenAITTSLoader : WebVoiceLoaderBase
    {
        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "OpenAI";
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

        public string ApiKey;
        public string Voice = "nova";
        public float Speed = 1.0f;

        public void Configure(string apiKey, string voice, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            Voice = string.IsNullOrEmpty(Voice) || overwrite ? voice : Voice;
        }

        // See API document: https://platform.openai.com/docs/api-reference/audio/createSpeech
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            if (string.IsNullOrEmpty(voice.Text.Replace(" ", "").Replace("\n", "").Trim()))
            {
                Debug.LogWarning("Query for OpenAI TTS is empty");
                return null;
            }

            var headers = new Dictionary<string, string>()
            {
                { "Authorization", $"Bearer {ApiKey}"  },
                { "Content-Type", "application/json" }
            };

            var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Dictionary<string, object>() {
                { "model", "tts-1" },
                { "input", voice.Text },
                { "voice", Voice },
                { "response_format", "mp3" },   // opus, aac and flac is not supported by Unity
                { "speed", Speed }
            }));

#if UNITY_WEBGL && !UNITY_EDITOR
            return await DownloadAudioClipWebGLAsync("https://api.openai.com/v1/audio/speech", data, headers, token);
#else
            return await DownloadAudioClipNativeAsync("https://api.openai.com/v1/audio/speech", data, headers, token);
#endif
        }

        protected async UniTask<AudioClip> DownloadAudioClipNativeAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                www.timeout = Timeout;
                www.method = "POST";

                www.SetRequestHeader("Authorization", headers["Authorization"]);
                www.SetRequestHeader("Content-Type", headers["Content-Type"]);

                www.uploadHandler = new UploadHandlerRaw(data);

                await www.SendWebRequest().ToUniTask();

                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"Error occured while processing text-to-speech voice: {www.error}");
                }
                else if (www.isDone)
                {
                    return DownloadHandlerAudioClip.GetContent(www);
                }
            }
            return null;
        }

        protected async UniTask<AudioClip> DownloadAudioClipWebGLAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            Debug.LogWarning("Unity doesn't support playback MP3 on WebGL.");

            var resp = await client.PostBytesAsync(url, data, headers, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(resp.Data, 1, 8000);
        }
    }
}

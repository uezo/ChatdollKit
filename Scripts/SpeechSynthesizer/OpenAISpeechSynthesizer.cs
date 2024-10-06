using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
#if UNITY_WEBGL && !UNITY_EDITOR
using ChatdollKit.Network;
using ChatdollKit.IO;
#endif

namespace ChatdollKit.SpeechSynthesizer
{
    public class OpenAISpeechSynthesizer : SpeechSynthesizerBase
    {
        public bool _IsEnabled = true;
        public override bool IsEnabled
        {
            get
            {
                return _IsEnabled;
            }
            set
            {
                _IsEnabled = value;
            }
        }

        public string ApiKey;
        public string Voice = "nova";
        public float Speed = 1.0f;

#if UNITY_WEBGL && !UNITY_EDITOR
        private ChatdollHttp client;

        private void Start()
        {
            client = new ChatdollHttp(Timeout);
        }
#endif

        // See API document: https://platform.openai.com/docs/api-reference/audio/createSpeech
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            if (string.IsNullOrEmpty(text.Replace(" ", "").Replace("\n", "").Trim()))
            {
                Debug.LogWarning("Query for OpenAI TTS is empty");
                return null;
            }

            var headers = new Dictionary<string, string>()
            {
                { "Authorization", $"Bearer {ApiKey}"  },
                { "Content-Type", "application/json" }
            };

#if UNITY_WEBGL && !UNITY_EDITOR
            var format = "pcm";
#else
            var format = "mp3";
#endif
            var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Dictionary<string, object>() {
                { "model", "tts-1" },
                { "input", text },
                { "voice", Voice },
                { "response_format", format },
                { "speed", Speed }
            }));

            return await DownloadAudioClipAsync("https://api.openai.com/v1/audio/speech", data, headers, token);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        protected async UniTask<AudioClip> DownloadAudioClipAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            // https://platform.openai.com/docs/guides/text-to-speech/supported-output-formats
            var resp = await client.PostBytesAsync(url, data, headers, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(resp.Data, 1, 24000);
        }
#else
        protected async UniTask<AudioClip> DownloadAudioClipAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
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
                    Debug.LogError($"Error occured while processing OpenAI text-to-speech: {ex}");
                    return null;
                }

                return DownloadHandlerAudioClip.GetContent(www);
            }
        }
#endif
    }
}

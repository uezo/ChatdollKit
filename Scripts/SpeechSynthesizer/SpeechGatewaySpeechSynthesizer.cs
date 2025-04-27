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
    public class SpeechGatewaySpeechSynthesizer : SpeechSynthesizerBase
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

        public string EndpointUrl;
        public string ServiceName;
        public string Language;
        public string Speaker;
        public AudioType AudioType = AudioType.MPEG;
        public bool AddWaveHeader = false;
        public int WebGLWaveSampleRate = 44100;


#if UNITY_WEBGL && !UNITY_EDITOR
        private ChatdollHttp client;

        private void Start()
        {
            client = new ChatdollHttp(Timeout);
        }
#endif

        protected override string GetCacheKey(string text, Dictionary<string, object> parameters)
        {
            // Make cache key
            var style = parameters.ContainsKey("style") && !string.IsNullOrEmpty((string)parameters["style"])
                ? (string)parameters["style"] : string.Empty;

            return $"{ServiceName}/{Speaker}/{style}: {text}";
        }

        // See API document: https://github.com/uezo/speech-gateway?tab=readme-ov-file#parameters
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            if (string.IsNullOrEmpty(text.Replace(" ", "").Replace("\n", "").Trim()))
            {
                Debug.LogWarning("Query for SpeechGateway TTS is empty");
                return null;
            }

            var headers = new Dictionary<string, string>()
            {
                { "Content-Type", "application/json" }
            };

            var data = new Dictionary<string, object>() {
                { "text", text }
            };

            data["language"] = parameters.ContainsKey("language") ? parameters["language"] as string : Language;
            if (data["language"] as string == Language)
            {
                data["speaker"] = Speaker;
                data["service_name"] = ServiceName;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            var format = "wav";
#else
            var format = AudioType == AudioType.MPEG ? "mp3" : "wav";
#endif

            return await DownloadAudioClipAsync(
                EndpointUrl + $"/tts?x_audio_format={format}",
                System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data)),
                headers,
                token
            );
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        protected async UniTask<AudioClip> DownloadAudioClipAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            var resp = await client.PostBytesAsync(url, data, headers, cancellationToken: token);
            if (AddWaveHeader)
            {
                return AudioConverter.PCMToAudioClip(resp.Data, 1, WebGLWaveSampleRate);
            }
            else
            {
                return AudioConverter.PCMToAudioClip(resp.Data);
            }
        }
#else
        protected async UniTask<AudioClip> DownloadAudioClipAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType))
            {
                www.timeout = Timeout;
                www.method = "POST";

                www.SetRequestHeader("Content-Type", headers["Content-Type"]);

                www.uploadHandler = new UploadHandlerRaw(data);

                try
                {
                    await www.SendWebRequest().ToUniTask(cancellationToken: token);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occured while processing SpeechGateway text-to-speech: {ex}");
                    return null;
                }

                return DownloadHandlerAudioClip.GetContent(www);
            }
        }
#endif
    }
}

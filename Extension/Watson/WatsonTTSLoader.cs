using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Model;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Watson
{
    public class WatsonTTSLoader : WebVoiceLoaderBase
    {
        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "Watson";
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

        public string ApiKey = string.Empty;
        public string BaseUrl = string.Empty;
        public string SpeakerName = "ja-JP_EmiV3Voice";
        public AudioType AudioType = AudioType.OGGVORBIS;

        public void Configure(string apiKey, string baseUrl, string speakerName, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            BaseUrl = string.IsNullOrEmpty(BaseUrl) || overwrite ? baseUrl : BaseUrl;
            SpeakerName = string.IsNullOrEmpty(SpeakerName) || overwrite ? speakerName : SpeakerName;
        }

        // See API document: https://cloud.ibm.com/apidocs/text-to-speech#synthesize
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(BaseUrl))
            {
                Debug.LogError("API Key or Language are missing from WatsonTTSLoader");
            }

            var url = $"{BaseUrl}/v1/synthesize?voice={voice.GetTTSParam("speakerName") as string ?? SpeakerName}";

            var headers = new Dictionary<string, string>()
            {
                { "Authorization", client.GetBasicAuthenticationHeaderValue("apikey", ApiKey).ToString() },
                { "Content-Type", "application/json" },
                { "Accept", AudioType == AudioType.OGGVORBIS ? "audio/ogg;codecs=vorbis" : "audio/l16;rate=8000" }    // Ogg Vorbis or Wave(mono/8KHz)
            };

            var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Dictionary<string, string>() { { "text", voice.Text } }));

#if UNITY_WEBGL && !UNITY_EDITOR
            return await DownloadAudioClipWebGLAsync(url, data, headers, token);
#else
            return await DownloadAudioClipNativeAsync(url, data, headers, token);
#endif
        }

        protected async UniTask<AudioClip> DownloadAudioClipNativeAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.OGGVORBIS))
            {
                www.timeout = Timeout;
                www.method = "POST";

                // Header
                www.SetRequestHeader("Authorization", headers["Authorization"]);
                www.SetRequestHeader("Content-Type", headers["Content-Type"]);
                www.SetRequestHeader("Accept", headers["Accept"]);

                // Body
                www.uploadHandler = new UploadHandlerRaw(data);

                // Send request
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
            var resp = await client.PostBytesAsync(url, data, headers, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(resp.Data, 1, 8000);
        }
    }
}

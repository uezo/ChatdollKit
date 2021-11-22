using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using ChatdollKit.Model;
using ChatdollKit.Network;

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

        private ChatdollHttp client = new ChatdollHttp();

        private void OnDestroy()
        {
            client?.Dispose();
        }

        public void Configure(string apiKey, string baseUrl, string speakerName, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            BaseUrl = string.IsNullOrEmpty(BaseUrl) || overwrite ? baseUrl : BaseUrl;
            SpeakerName = string.IsNullOrEmpty(SpeakerName) || overwrite ? speakerName : SpeakerName;
        }

        protected override async Task<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(BaseUrl))
            {
                Debug.LogError("API Key or Language are missing from WatsonTTSLoader");
            }

            var url = $"{BaseUrl}/v1/synthesize?voice={voice.GetTTSParam("speakerName") as string ?? SpeakerName}";
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.OGGVORBIS))
            {
                www.timeout = Timeout;
                www.method = "POST";

                // Header
                www.SetRequestHeader("Authorization", client.GetBasicAuthenticationHeaderValue("apikey", ApiKey).ToString());
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Accept", "audio/ogg;codecs=vorbis");

                // Body
                www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Dictionary<string, string>(){{"text", voice.Text}})));

                // Send request
                await www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
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
    }
}

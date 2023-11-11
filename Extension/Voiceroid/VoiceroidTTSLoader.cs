using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.IO;
using ChatdollKit.Model;

namespace ChatdollKit.Extension
{
    public class VoiceroidTTSLoader : WebVoiceLoaderBase
    {
        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "Voiceroid";
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
        public string BaseUrl;
        public AudioType AudioType = AudioType.WAV;

        [Header("Voice Settings")]
        [Range(0.0f, 2.0f)]
        public float Volume = 1.0f;
        [Range(0.5f, 4.0f)]
        public float Speed = 1.0f;
        [Range(0.5f, 2.0f)]
        public float Pitch = 1.0f;
        [Range(0.0f, 2.0f)]
        public float Emphasis = 1.0f;
        [Range(80.0f, 500.0f)]
        public float PauseMiddle = 150.0f;
        [Range(100.0f, 2000.0f)]
        public float PauseLong = 370.0f;
        [Range(200.0f, 10000.0f)]
        public float PauseSentence = 800.0f;
        [Range(0.0f, 5.0f)]
        public float MasterVolume = 1.0f;

        protected static List<string> voiceParameterKeys = new List<string>() { "Volume", "Speed", "Pitch", "Emphasis" };

        protected override async void Start()
        {
            base.Start();
            await UpdateSettingsAsync();
        }

        // Get audio clip from pyvcroid2-api
        // https://github.com/uezo/pyvcroid2-api
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            var headers = new Dictionary<string, string>() { { "Content-Type", "application/json" } };
            var request = new VoiceroidRequest(voice.Text, AudioType == AudioType.MPEG ? "mp3" : "wav");
            var voiceParameters = new Dictionary<string, float>();
            foreach (var key in voiceParameterKeys)
            {
                if (voice.GetTTSParam(key) != null)
                {
                    voiceParameters.Add(key, (float)voice.GetTTSParam(key));
                }
            }
            if (voiceParameters.Count > 0)
            {
                request.VoiceParameters = voiceParameters;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return await DownloadAudioClipWebGLAsync(request, headers, token);
#else
            return await DownloadAudioClipNativeAsync(request, headers, token);
#endif
        }

        private async UniTask<AudioClip> DownloadAudioClipNativeAsync(VoiceroidRequest request, Dictionary<string, string> headers, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(BaseUrl + "/api/speech", AudioType))
            {
                www.timeout = Timeout;
                www.method = "POST";

                www.SetRequestHeader("Content-Type", headers["Content-Type"]);
                www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request)));

                await www.SendWebRequest().ToUniTask();

                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"Error occured while processing voiceroid text-to-speech: {www.error}");
                }
                else if (www.isDone)
                {
                    return DownloadHandlerAudioClip.GetContent(www);
                }
            }
            return null;
        }

        private async UniTask<AudioClip> DownloadAudioClipWebGLAsync(VoiceroidRequest request, Dictionary<string, string> headers, CancellationToken token)
        {
            request.Format = "wav";
            var resp = await client.PostJsonAsync(BaseUrl + "/api/speech", request, headers, cancellationToken: token);

            return AudioConverter.PCMToAudioClip(resp.Data, searchDataChunk: true);
        }

        private async UniTask UpdateSettingsAsync()
        {
            var request = new VoiceroidUpdateSettingsRequest(Volume, Speed, Pitch, Emphasis, PauseMiddle, PauseLong, PauseSentence, MasterVolume);
            await client.PatchJsonAsync(BaseUrl + "/api/settings", request);
        }

        class VoiceroidUpdateSettingsRequest
        {
            [JsonProperty("volume")]
            public float Volume { get; set; }
            [JsonProperty("speed")]
            public float Speed { get; set; }
            [JsonProperty("pitch")]
            public float Pitch { get; set; }
            [JsonProperty("emphasis")]
            public float Emphasis { get; set; }
            [JsonProperty("pause_middle")]
            public float PauseMiddle { get; set; }
            [JsonProperty("pause_long")]
            public float PauseLong { get; set; }
            [JsonProperty("pause_sentence")]
            public float PauseSentence { get; set; }
            [JsonProperty("master_volume")]
            public float MasterVolume { get; set; }

            public VoiceroidUpdateSettingsRequest(float volume, float speed, float pitch, float emphasis, float pauseMiddle, float pauseLong, float pauseSentence, float masterVolume)
            {
                Volume = volume;
                Speed = speed;
                Pitch = pitch;
                Emphasis = emphasis;
                PauseMiddle = pauseMiddle;
                PauseLong = pauseLong;
                PauseSentence = pauseSentence;
                MasterVolume = masterVolume;
            }
        }

        class VoiceroidRequest
        {
            [JsonProperty("text")]
            public string Text { get; set; }
            [JsonProperty("format")]
            public string Format { get; set; }
            [JsonProperty("params")]
            public Dictionary<string, float> VoiceParameters { get; set; }

            public VoiceroidRequest(string text, string format)
            {
                Text = text;
                Format = format;
                VoiceParameters = null;
            }
        }
    }
}

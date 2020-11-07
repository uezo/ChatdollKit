using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using ChatdollKit.Model;
using ChatdollKit.Network;


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
        public string EndpointUrl;
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

        // Get audio clip from Voiceroid Daemon
        // https://github.com/Nkyoku/voiceroid_daemon
        protected override async Task<AudioClip> DownloadAudioClipAsync(Voice voice)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(EndpointUrl, AudioType))
            {
                www.timeout = 10;
                www.method = "POST";

                // Header
                www.SetRequestHeader("Content-Type", "application/json");

                // Body
                var request = new VoiceroidRequest(voice, this);
                var text = JsonConvert.SerializeObject(request);
                www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(text));

                // Send request
                await www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogError($"Error occured while processing voiceroid text-to-speech: {www.error}");
                }
                else if (www.isDone)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip != null)
                    {
                        audioCache[voice.CacheKey] = clip;
                    }
                    return clip;
                }
            }
            return null;
        }

        class VoiceroidRequest
        {
            public string Text { get; set; }
            public string Kana { get; set; }
            public Dictionary<string, float> Speaker { get; set; }

            public VoiceroidRequest(Voice voice, VoiceroidTTSLoader loader)
            {
                Text = voice.Text;
                Kana = (string)voice.GetTTSParam("Kana");
                Speaker = new Dictionary<string, float>
                {
                    ["Volume"] = (float)(voice.GetTTSParam("Volume") ?? loader.Volume),
                    ["Speed"] = (float)(voice.GetTTSParam("Speed") ?? loader.Speed),
                    ["Pitch"] = (float)(voice.GetTTSParam("Pitch") ?? loader.Pitch),
                    ["Emphasis"] = (float)(voice.GetTTSParam("Emphasis") ?? loader.Emphasis),

                    // PauseMiddle / PauseLong / PauseSentence doesn't work :(
                };
            }
        }
    }
}

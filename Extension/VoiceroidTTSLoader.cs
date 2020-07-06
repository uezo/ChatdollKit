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
        public bool _IsDefault = false;
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
                var request = new VoiceroidRequest(voice);
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
                    if (!string.IsNullOrEmpty(voice.Name))
                    {
                        // Cache if name is set
                        audioCache[voice.Name] = clip;
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

            public VoiceroidRequest(Voice voice)
            {
                Text = voice.Text;
                Kana = (string)voice.GetTTSParam("Kana");
                Speaker = new Dictionary<string, float>();
                if (voice.GetTTSParam("Volume") != null)
                {
                    Speaker["Volume"] = (float)voice.GetTTSParam("Volume");
                }
                if (voice.GetTTSParam("Speed") != null)
                {
                    Speaker["Speed"] = (float)voice.GetTTSParam("Speed");
                }
                if (voice.GetTTSParam("Pitch") != null)
                {
                    Speaker["Pitch"] = (float)voice.GetTTSParam("Pitch");
                }
                if (voice.GetTTSParam("Emphasis") != null)
                {
                    Speaker["Emphasis"] = (float)voice.GetTTSParam("Emphasis");
                }
                if (voice.GetTTSParam("PauseMiddle") != null)
                {
                    Speaker["PauseMiddle"] = (float)voice.GetTTSParam("PauseMiddle");
                }
                if (voice.GetTTSParam("PauseLong") != null)
                {
                    Speaker["PauseLong"] = (float)voice.GetTTSParam("PauseLong");
                }
                if (voice.GetTTSParam("PauseSentence") != null)
                {
                    Speaker["PauseSentence"] = (float)voice.GetTTSParam("PauseSentence");
                }
            }
        }
    }
}

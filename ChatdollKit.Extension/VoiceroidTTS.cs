using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using ChatdollKit.Model;
using ChatdollKit.Network;


namespace ChatdollKit.Extension
{
    public class VoiceroidTTS
    {
        public string EndpointUrl { get; set; }

        private Dictionary<string, AudioClip> audioCache;

        public VoiceroidTTS(string endpointUrl)
        {
            EndpointUrl = endpointUrl;
        }

        // Get audio clip from Voiceroid Daemon
        // https://github.com/Nkyoku/voiceroid_daemon
        public async Task<AudioClip> GetAudioClipFromTTS(Voice voice, AudioType audioType = AudioType.WAV)
        {
            // Return from cache when name is set and it's cached
            if (!string.IsNullOrEmpty(voice.Name))
            {
                if (audioCache.ContainsKey(voice.Name))
                {
                    return audioCache[voice.Name];
                }
            }

            using (var www = UnityWebRequestMultimedia.GetAudioClip(EndpointUrl, audioType))
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
    }

    class VoiceroidRequest
    {
        public string Text { get; set; }
        public string Kana { get; set; }
        public Dictionary<string, float> Speaker { get; set; }

        public VoiceroidRequest(Voice voice)
        {
            Text = voice.Text;
            Kana = voice.GetTTSOption("Kana");
            Speaker = new Dictionary<string, float>();
            if (!string.IsNullOrEmpty(voice.GetTTSOption("Volume")))
            {
                Speaker["Volume"] = float.Parse(voice.GetTTSOption("Volume"));
            }
            if (!string.IsNullOrEmpty(voice.GetTTSOption("Speed")))
            {
                Speaker["Speed"] = float.Parse(voice.GetTTSOption("Speed"));
            }
            if (!string.IsNullOrEmpty(voice.GetTTSOption("Pitch")))
            {
                Speaker["Pitch"] = float.Parse(voice.GetTTSOption("Pitch"));
            }
            if (!string.IsNullOrEmpty(voice.GetTTSOption("Emphasis")))
            {
                Speaker["Emphasis"] = float.Parse(voice.GetTTSOption("Emphasis"));
            }
            if (!string.IsNullOrEmpty(voice.GetTTSOption("PauseMiddle")))
            {
                Speaker["PauseMiddle"] = float.Parse(voice.GetTTSOption("PauseMiddle"));
            }
            if (!string.IsNullOrEmpty(voice.GetTTSOption("PauseLong")))
            {
                Speaker["PauseLong"] = float.Parse(voice.GetTTSOption("PauseLong"));
            }
            if (!string.IsNullOrEmpty(voice.GetTTSOption("PauseSentence")))
            {
                Speaker["PauseSentence"] = float.Parse(voice.GetTTSOption("PauseSentence"));
            }
        }
    }
}

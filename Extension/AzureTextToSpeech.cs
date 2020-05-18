using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using ChatdollKit.Model;
using ChatdollKit.Network;


namespace ChatdollKit.Extension
{
    public class AzureTextToSpeech
    {
        public string ApiKey { get; set; }
        public string Region { get; set; }
        public string Language { get; set; }
        public string Gender { get; set; }
        public string SpeakerName { get; set; }
        public AudioType AudioType { get; set; }
        private Dictionary<string, AudioClip> audioCache { get; set; }

        public AzureTextToSpeech(string apiKey, string region = "japanwest", string language = "ja-JP", string gender = "Female", string speakerName = "ja-JP-HarukaRUS", AudioType audioType = AudioType.WAV)
        {
            ApiKey = apiKey;
            Region = region;
            Language = language;
            Gender = gender;
            SpeakerName = speakerName;
            AudioType = audioType;
            audioCache = new Dictionary<string, AudioClip>();
        }

        // Get audio clip from using Azure Text-to-Speech
        public async Task<AudioClip> GetAudioClipFromTTS(Voice voice)
        {
            if (!string.IsNullOrEmpty(voice.Name) && audioCache.ContainsKey(voice.Name))
            {
                // Use cache when name is set and it's cached
                return audioCache[voice.Name];
            }

            var url = $"https://{Region}.tts.speech.microsoft.com/cognitiveservices/v1";
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType))
            {
                www.timeout = 10;
                www.method = "POST";

                // Header
                www.SetRequestHeader("X-Microsoft-OutputFormat", AudioType == AudioType.WAV ? "riff-16khz-16bit-mono-pcm" : "audio-16khz-128kbitrate-mono-mp3");
                www.SetRequestHeader("Content-Type", "application/ssml+xml");
                www.SetRequestHeader("Ocp-Apim-Subscription-Key", ApiKey);

                // Body
                var ttsLanguage = voice.GetTTSOption("language") ?? Language;
                var ttsGender = voice.GetTTSOption("gender") ?? Gender;
                var ttsSpeakerName = voice.GetTTSOption("speakerName") ?? SpeakerName;
                var text = $"<speak version='1.0' xml:lang='{ttsLanguage}'><voice xml:lang='{ttsLanguage}' xml:gender='{ttsGender}' name='{ttsSpeakerName}'>{voice.Text}</voice></speak>";
                www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(text));

                // Send request
                await www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogError($"Error occured while processing text-to-speech voice: {www.error}");
                }
                else if (www.isDone)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(www);

                    if (!string.IsNullOrEmpty(voice.Name) && clip != null)
                    {
                        // Cache if name is set
                        audioCache[voice.Name] = clip;
                    }

                    return clip;
                }
            }
            return null;
        }

        // Load audio clip from web
        public async Task<bool> LoadAudioClipFromTTS(string name, string text, Dictionary<string, string> ttsOptions = null)
        {
            var voice = new Voice(name, 0.0f, 0.0f, text, string.Empty, ttsOptions, VoiceSource.TTS);
            return await GetAudioClipFromTTS(voice) == null ? false : true;
        }
    }
}

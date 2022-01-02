using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;

namespace ChatdollKit.Extension.Azure
{
    public class AzureTTSLoader : WebVoiceLoaderBase
    {
        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "Azure";
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
        public string Region = "japanwest";
        public string Language = "ja-JP";
        public string Gender = "Female";
        public string SpeakerName = "ja-JP-HarukaRUS";
        public AudioType AudioType = AudioType.MPEG;

        public void Configure(string apiKey, string language, string gender, string speakerName, string region, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            Language = string.IsNullOrEmpty(Language) || overwrite ? language : Language;
            Gender = string.IsNullOrEmpty(Gender) || overwrite ? gender : Gender;
            SpeakerName = string.IsNullOrEmpty(SpeakerName) || overwrite ? speakerName : SpeakerName;
            Region = string.IsNullOrEmpty(Region) || overwrite ? region : Region;
        }

        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Region) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key, Region or Language are missing from AzureTTSLoader");
            }

            var url = $"https://{Region}.tts.speech.microsoft.com/cognitiveservices/v1";
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType))
            {
                www.timeout = Timeout;
                www.method = "POST";

                // Header
                www.SetRequestHeader("X-Microsoft-OutputFormat", AudioType == AudioType.MPEG ? "audio-16khz-32kbitrate-mono-mp3" : "riff-16khz-16bit-mono-pcm");
                www.SetRequestHeader("Content-Type", "application/ssml+xml");
                www.SetRequestHeader("Ocp-Apim-Subscription-Key", ApiKey);

                // Body
                var ttsLanguage = voice.GetTTSParam("language") as string ?? Language;
                var ttsGender = voice.GetTTSParam("gender") as string ?? Gender;
                var ttsSpeakerName = voice.GetTTSParam("speakerName") as string ?? SpeakerName;
                var text = $"<speak version='1.0' xml:lang='{ttsLanguage}'><voice xml:lang='{ttsLanguage}' xml:gender='{ttsGender}' name='{ttsSpeakerName}'>{voice.Text}</voice></speak>";
                www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(text));

                // Send request
                await www.SendWebRequest().ToUniTask();

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

using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using ChatdollKit.Model;
using ChatdollKit.Network;

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

        protected override async Task<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            var url = $"https://{Region}.tts.speech.microsoft.com/cognitiveservices/v1";
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType))
            {
                www.timeout = Timeout;
                www.method = "POST";

                // Header
                www.SetRequestHeader("X-Microsoft-OutputFormat", AudioType == AudioType.MPEG ? "audio-16khz-128kbitrate-mono-mp3" : "riff-16khz-16bit-mono-pcm");
                www.SetRequestHeader("Content-Type", "application/ssml+xml");
                www.SetRequestHeader("Ocp-Apim-Subscription-Key", ApiKey);

                // Body
                var ttsLanguage = voice.GetTTSParam("language") as string ?? Language;
                var ttsGender = voice.GetTTSParam("gender") as string ?? Gender;
                var ttsSpeakerName = voice.GetTTSParam("speakerName") as string ?? SpeakerName;
                var text = $"<speak version='1.0' xml:lang='{ttsLanguage}'><voice xml:lang='{ttsLanguage}' xml:gender='{ttsGender}' name='{ttsSpeakerName}'>{voice.Text}</voice></speak>";
                www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(text));

                // Send request
                await www.SendWebRequest();

                Debug.LogWarning(www.downloadedBytes);

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

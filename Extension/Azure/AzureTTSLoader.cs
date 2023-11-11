using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;
using ChatdollKit.IO;

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
        public string SpeakerName = "ja-JP-AoiNeural";
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

            var headers = new Dictionary<string, string>()
            {
                { "X-Microsoft-OutputFormat", AudioType == AudioType.MPEG ? "audio-16khz-32kbitrate-mono-mp3" : "riff-16khz-16bit-mono-pcm" },    // MP3 or Wave
                { "Content-Type", "application/ssml+xml" },
                { "Ocp-Apim-Subscription-Key", ApiKey }
            };

            var ttsLanguage = voice.GetTTSParam("language") as string ?? Language;
            var ttsGender = voice.GetTTSParam("gender") as string ?? Gender;
            var ttsSpeakerName = voice.GetTTSParam("speakerName") as string ?? SpeakerName;
            var text = $"<speak version='1.0' xml:lang='{ttsLanguage}'><voice xml:lang='{ttsLanguage}' xml:gender='{ttsGender}' name='{ttsSpeakerName}'>{voice.Text}</voice></speak>";
            var data = System.Text.Encoding.UTF8.GetBytes(text);

#if UNITY_WEBGL && !UNITY_EDITOR
            return await DownloadAudioClipWebGLAsync(url, data, headers, token);
#else
            return await DownloadAudioClipNativeAsync(url, data, headers, token);
#endif
        }

        protected async UniTask<AudioClip> DownloadAudioClipNativeAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType))
            {
                www.timeout = Timeout;
                www.method = "POST";

                // Header
                www.SetRequestHeader("X-Microsoft-OutputFormat", headers["X-Microsoft-OutputFormat"]);
                www.SetRequestHeader("Content-Type", headers["Content-Type"]);
                www.SetRequestHeader("Ocp-Apim-Subscription-Key", headers["Ocp-Apim-Subscription-Key"]);

                // Body
                www.uploadHandler = new UploadHandlerRaw(data);

                // Send request
                await www.SendWebRequest();

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
            return AudioConverter.PCMToAudioClip(resp.Data, searchDataChunk: true);
        }
    }
}

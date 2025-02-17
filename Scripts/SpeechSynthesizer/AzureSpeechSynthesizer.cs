using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
#if UNITY_WEBGL && !UNITY_EDITOR
using ChatdollKit.Network;
using ChatdollKit.IO;
#endif

namespace ChatdollKit.SpeechSynthesizer
{
    public class AzureSpeechSynthesizer : SpeechSynthesizerBase
    {
        public bool _IsEnabled = true;
        public override bool IsEnabled
        {
            get
            {
                return _IsEnabled;
            }
            set
            {
                _IsEnabled = value;
            }
        }

        public string ApiKey;
        public string Region = "japanwest";
        public string Language = "ja-JP";
        public string Gender = "Female";
        public string SpeakerName = "ja-JP-AoiNeural";
        public Dictionary<string, string> SpeakerMap = new ();
        public AudioType AudioType = AudioType.MPEG;

#if UNITY_WEBGL && !UNITY_EDITOR
        private ChatdollHttp client;

        private void Start()
        {
            client = new ChatdollHttp(Timeout);
        }
#endif

        protected override async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token)
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

            string language;
            string speaker;
            if (parameters.ContainsKey("language"))
            {
                language = parameters["language"] as string;
                if (SpeakerMap.ContainsKey(language))
                {
                    speaker = SpeakerMap[language];
                }
                else
                {
                    speaker = SpeakerName;
                }
            }
            else
            {
                language = Language;
                speaker = SpeakerName;
            }

            var textML = $"<speak version='1.0' xml:lang='{language}'><voice xml:lang='{language}' name='{speaker}'>{text}</voice></speak>";
            var data = System.Text.Encoding.UTF8.GetBytes(textML);

            return await DownloadAudioClipAsync(url, data, headers, token);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        protected async UniTask<AudioClip> DownloadAudioClipAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            var resp = await client.PostBytesAsync(url, data, headers, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(resp.Data, searchDataChunk: true);
        }
#else
        protected async UniTask<AudioClip> DownloadAudioClipAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
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
                try
                {
                    await www.SendWebRequest().ToUniTask(cancellationToken: token);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occured while processing Azure text-to-speech: {ex}");
                    return null;
                }

                return DownloadHandlerAudioClip.GetContent(www);
            }
        }
#endif
    }
}

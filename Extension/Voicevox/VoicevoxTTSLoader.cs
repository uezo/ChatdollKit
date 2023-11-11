using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Voicevox
{
    public class VoicevoxTTSLoader : WebVoiceLoaderBase
    {
        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "Voicevox";
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

        [Header("Voice Settings")]
        public int Speaker = 2;
        [SerializeField]
        protected bool printSupportedSpeakers;

        protected override void Start()
        {
            base.Start();
            if (printSupportedSpeakers)
            {
                _ = ListSpeakersAsync(CancellationToken.None);
            }
        }

        public void Configure(string endpointUrl, bool overwrite = false)
        {
            EndpointUrl = string.IsNullOrEmpty(EndpointUrl) || overwrite ? endpointUrl : EndpointUrl;
        }

        // Get audio clip from VOICEVOX engine
        // https://github.com/Hiroshiba/voicevox_engine
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            // Convert text to query for TTS from VOICEVOX server
            var queryResp = await client.PostFormAsync(
                EndpointUrl + $"/audio_query?speaker={(decimal)Speaker}&text={UnityWebRequest.EscapeURL(voice.Text, Encoding.UTF8)}",
                new Dictionary<string, string>(), cancellationToken: token);

            var audioQuery = queryResp.Text;

            if (string.IsNullOrEmpty(audioQuery))
            {
                Debug.LogError("Query for VOICEVOX is empty");
                return null;
            }

            if (token.IsCancellationRequested) { return null; };

            // Get audio data from VOICEBOX server
            var url = EndpointUrl + $"/synthesis?speaker={(decimal)Speaker}";
            var headers = new Dictionary<string, string>() { { "Content-Type", "application/json" } };
            var data = Encoding.UTF8.GetBytes(audioQuery);

#if UNITY_WEBGL && !UNITY_EDITOR
            return await DownloadAudioClipWebGLAsync(url, data, headers, token);
#else
            return await DownloadAudioClipNativeAsync(url, data, headers, token);
#endif
        }

        protected async UniTask<AudioClip> DownloadAudioClipNativeAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                www.timeout = Timeout;
                www.method = "POST";

                // Header
                www.SetRequestHeader("Content-Type", headers["Content-Type"]);

                // Body
                www.uploadHandler = new UploadHandlerRaw(data);

                // Send request
                await www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"Error occured while processing voicevox text-to-speech: {www.error}");
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
            var audioResp = await client.PostBytesAsync(url, data, headers, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(audioResp.Data);
        }

        public async UniTask ListSpeakersAsync(CancellationToken token)
        {
            Debug.Log("==== Supported speakers ====");

            foreach (var s in await GetSpearkersAsync(token))
            {
                Debug.Log($"{s.Key}: {s.Value}");
            }
        }

        public async UniTask<Dictionary<string, int>> GetSpearkersAsync(CancellationToken token)
        {
            var speakers = new Dictionary<string, int>();

            var speakerResponses = await client.GetJsonAsync<List<SpeakersResponse>>(
                EndpointUrl + $"/speakers",
                cancellationToken: token);

            foreach (var sr in speakerResponses)
            {
                var name = sr.name;
                foreach (var style in sr.styles)
                {
                    speakers.Add($"{name}_{style.name}", style.id);
                }
            }

            return speakers;
        }

        protected class SpeakersResponse
        {
            public string name;
            public List<SpeakerStyle> styles;
        }

        protected class SpeakerStyle
        {
            public string name;
            public int id;
        }
    }
}

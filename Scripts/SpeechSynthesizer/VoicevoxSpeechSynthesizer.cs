using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;
using ChatdollKit.Network;

namespace ChatdollKit.SpeechSynthesizer
{
    public class VoicevoxSpeechSynthesizer : SpeechSynthesizerBase
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

        public string EndpointUrl;

        [Header("Voice Settings")]
        public int Speaker = 2;
        [SerializeField]
        protected bool printSupportedSpeakers;

        public List<VoiceStyle> VoiceStyles;

        private ChatdollHttp client;

        private void Start()
        {
            client = new ChatdollHttp(Timeout);
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
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            var textToSpeech = text.Replace(" ", "").Replace("\n", "").Trim();
            if (string.IsNullOrEmpty(textToSpeech) || textToSpeech == "」") return null;

            // Apply style
            var inlineSpeaker = Speaker;
            if (parameters.ContainsKey("style"))
            {
                var voiceStyle = parameters["style"] as string;
                if (!string.IsNullOrEmpty(voiceStyle))
                {
                    foreach (var style in VoiceStyles)
                    {
                        if (style.VoiceStyleValue == voiceStyle)
                        {
                            inlineSpeaker = style.VoiceVoxSpeaker;
                            break;
                        }
                    }
                }
            }

            // Convert text to query for TTS from VOICEVOX server
            var queryResp = await client.PostFormAsync(
                EndpointUrl + $"/audio_query?speaker={(decimal)inlineSpeaker}&text={UnityWebRequest.EscapeURL(text, Encoding.UTF8)}",
                new Dictionary<string, string>(), cancellationToken: token);

            var audioQuery = queryResp.Text;

            if (string.IsNullOrEmpty(audioQuery))
            {
                Debug.LogError("Query for VOICEVOX is empty");
                return null;
            }

            if (token.IsCancellationRequested) { return null; };

            // Get audio data from VOICEBOX server
            var url = EndpointUrl + $"/synthesis?speaker={(decimal)inlineSpeaker}";
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
                try
                {
                    await www.SendWebRequest().ToUniTask(cancellationToken: token);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occured while processing VOICEVOX text-to-speech: {ex}");
                    return null;
                }

                return DownloadHandlerAudioClip.GetContent(www);
            }
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

        private class SpeakersResponse
        {
            public string name;
            public List<SpeakerStyle> styles;
        }

        private class SpeakerStyle
        {
            public string name;
            public int id;
        }

        [Serializable]
        public class VoiceStyle
        {
            public string VoiceStyleValue;
            public int VoiceVoxSpeaker;
        }
    }
}

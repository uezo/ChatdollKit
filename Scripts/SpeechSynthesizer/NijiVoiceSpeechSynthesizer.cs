using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;
using ChatdollKit.Network;

namespace ChatdollKit.SpeechSynthesizer
{
    public class NijiVoiceSpeechSynthesizer : SpeechSynthesizerBase
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
        public string ApiKey;

        [Header("Voice Settings")]
        public string VoiceActorId = "dba2fa0e-f750-43ad-b9f6-d5aeaea7dc16";
        public float Speed = 1.0f;
        [SerializeField]
        private AudioType audioType = AudioType.WAV;

        public List<VoiceModelSpeed> VoiceModelSpeeds;

        [SerializeField]
        protected bool printSupportedSpeakers;

        private ChatdollHttp client;

        private void Start()
        {
            client = new ChatdollHttp(Timeout);
            if (printSupportedSpeakers)
            {
                _ = ListSpeakersAsync(CancellationToken.None);
            }
        }

        // Get audio clip from NijiVoice API
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            var textToSpeech = text.Replace(" ", "").Replace("\n", "").Trim();
            if (string.IsNullOrEmpty(textToSpeech) || textToSpeech == "」") return null;

            // Generate audio data on NijiVoice server
            var url = (string.IsNullOrEmpty(EndpointUrl) ? "https://api.nijivoice.com" : EndpointUrl) + $"/api/platform/v1/voice-actors/{VoiceActorId}/generate-voice";
            var speed = Speed > 0 ? Speed : VoiceModelSpeeds.FirstOrDefault(v => v.id == VoiceActorId)?.speed ?? 1.0f;
            var data = new Dictionary<string, string>() {
                { "script", text },
                { "speed", speed.ToString() },
                { "format", audioType == AudioType.MPEG ? "mp3" : "wav" },
            };
            var headers = new Dictionary<string, string>() { { "Content-Type", "application/json" }, { "x-api-key", ApiKey } };
            var generatedVoiceResponse = await client.PostJsonAsync<GeneratedVoiceResponse>(url, data, headers, cancellationToken: token);

#if UNITY_WEBGL && !UNITY_EDITOR
            return await DownloadAudioClipWebGLAsync(generatedVoiceResponse.generatedvoice.audioFileUrl, token);
#else
            return await DownloadAudioClipNativeAsync(generatedVoiceResponse.generatedvoice.audioFileUrl, token);
#endif
        }

        protected async UniTask<AudioClip> DownloadAudioClipNativeAsync(string url, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                www.timeout = Timeout;
                www.method = "GET";

                // Send request
                try
                {
                    await www.SendWebRequest().ToUniTask(cancellationToken: token);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occured while processing NijiVoice text-to-speech: {ex}");
                    return null;
                }

                return DownloadHandlerAudioClip.GetContent(www);
            }
        }

        protected async UniTask<AudioClip> DownloadAudioClipWebGLAsync(string url, CancellationToken token)
        {
            var audioResp = await client.GetAsync(url, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(audioResp.Data);
        }

        public async UniTask ListSpeakersAsync(CancellationToken token)
        {
            if (printSupportedSpeakers)
            {
                Debug.Log("==== Supported speakers ====");
            }

            VoiceModelSpeeds.Clear();
            foreach (var s in await GetSpearkersAsync(token))
            {
                if (printSupportedSpeakers)
                {
                    Debug.Log($"{s.Key}: {s.Value.name} ({s.Value.recommendedVoiceSpeed})");
                }
                VoiceModelSpeeds.Add(new VoiceModelSpeed(){ id = s.Key, speed = s.Value.recommendedVoiceSpeed });
            }
        }

        private async UniTask<Dictionary<string, VoiceActorData>> GetSpearkersAsync(CancellationToken token)
        {
            var speakers = new Dictionary<string, VoiceActorData>();
            var speakerResponse = await client.GetJsonAsync<SpeakersResponse>(
                (string.IsNullOrEmpty(EndpointUrl) ? "https://api.nijivoice.com" : EndpointUrl) + "/api/platform/v1/voice-actors",
                headers: new Dictionary<string, string>(){
                    { "x-api-key", ApiKey }
                },
                cancellationToken: token);

            foreach (var va in speakerResponse.voiceActors)
            {
                speakers.Add(va.id, va);
            }

            return speakers;
        }

        private class SpeakersResponse
        {
            public List<VoiceActorData> voiceActors;
        }

        [Serializable]
        public class VoiceStyle
        {
            public int id;
            public string style;
        }

        [Serializable]
        public class VoiceActorData
        {
            public string id;
            public string name;
            public string nameReading;
            public int age;
            public string gender;
            public int birthMonth;
            public int birthDay;
            public string smallImageUrl;
            public string mediumImageUrl;
            public string largeImageUrl;
            public string sampleVoiceUrl;
            public string sampleScript;
            public float recommendedVoiceSpeed;
            public List<VoiceStyle> voiceStyles;
        }

        [Serializable]
        public class VoiceModelSpeed
        {
            public string id;
            public float speed;
        }

        private class GeneratedVoiceResponse
        {
            public GeneratedVoice generatedvoice { get; set; }
        }

        private class GeneratedVoice
        {
            public string audioFileUrl { get; set; }
            public float duration { get; set; }
        }
    }
}

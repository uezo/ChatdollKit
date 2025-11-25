using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;
using ChatdollKit.Network;
using UnityEngine.UIElements.Experimental;

namespace ChatdollKit.SpeechSynthesizer
{
    public class KotodamaSpeechSynthesizer : SpeechSynthesizerBase
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

        public string EndpointUrl = "https://tts3.spiral-ai-app.com/api/tts_generate";

        [Header("Voice Settings")]
        public string ApiKey;
        public string SpeakerId = "Marlo";
        public string Style = "Neutral";
        public string Language;
        public AudioType AudioType = AudioType.WAV;
        public List<VoiceStyle> VoiceStyles = new()
        {
            new VoiceStyle("Neutral", "neutral"),
            new VoiceStyle("Joy", "happy"),
            new VoiceStyle("Sorrow", "sad"),
            new VoiceStyle("Fun", "laughing"),
            new VoiceStyle("Surprised", "surprised"),
        };

        private ChatdollHttp client;

        private void Start()
        {
            client = new ChatdollHttp(Timeout);
        }

        // Get audio clip from Kotodama API
        // https://spiralai.notion.site/Kotodama-API-28e7fe6ac353805ab242da38119b4f1f#28f7fe6ac35380989d41f1cf761285b4
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            var textToSpeech = text.Replace(" ", "").Replace("\n", "").Trim();
            if (string.IsNullOrEmpty(textToSpeech) || textToSpeech == "」") return null;

            // Apply style
            var decorationId = Style;
            if (parameters.ContainsKey("style"))
            {
                var voiceStyle = parameters["style"] as string;
                if (!string.IsNullOrEmpty(voiceStyle))
                {
                    foreach (var style in VoiceStyles)
                    {
                        if (style.VoiceStyleValue == voiceStyle)
                        {
                            decorationId = style.KotodamaDecorationId;
                            break;
                        }
                    }
                }
            }

            // Language
            var language = parameters.ContainsKey("language") ? parameters["language"] as string : Language;
            if (language.ToLower().Contains("en"))
            {
                decorationId += "_en";
            }

            // Make query
            var data = new Dictionary<string, string>() {
                { "text", text },
                { "speaker_id", SpeakerId },
                { "decoration_id", decorationId },
                { "audio_format", AudioType == AudioType.MPEG ? "mp3" : "wav" },
            };
            var headers = new Dictionary<string, string>() { { "Content-Type", "application/json" }, { "X-API-Key", ApiKey } };
            var ttsResponse = await client.PostJsonAsync<KotodamaTextToSpeechResponse>(
                EndpointUrl, data, headers, cancellationToken: token
            );

            if (!string.IsNullOrEmpty(ttsResponse.audios[0]))
            {
                var audioBin = Convert.FromBase64String(ttsResponse.audios[0]);
                return AudioConverter.PCMToAudioClip(audioBin);
            }
            else
            {
                return null;
            }
        }

        [Serializable]
        public class VoiceStyle
        {
            public string VoiceStyleValue;
            public string KotodamaDecorationId;

            public VoiceStyle(string voiceStyleValue, string kotodamaDecorationId)
            {
                VoiceStyleValue = voiceStyleValue;
                KotodamaDecorationId = kotodamaDecorationId;
            }
        }

        class KotodamaTextToSpeechResponse
        {
            public string[] audios;
        }
    }
}

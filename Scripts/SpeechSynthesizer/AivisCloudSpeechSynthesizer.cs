using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Network;
using ChatdollKit.IO;

namespace ChatdollKit.SpeechSynthesizer
{
    public class AivisCloudSpeechSynthesizer : SpeechSynthesizerBase
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


        [Header("Aivis Cloud Settings")]
        public string EndpointUrl;
        public string ApiKey;
        public string ModelUUID = "a59cb814-0083-4369-8542-f51a29e72af7";
        public string SpeakerUUID;
        public int StyleId = 0;
        public string StyleName;
        public string UserDictionaryUUID;
        public bool UseSSML = false;
        public string Language = "ja";
        public float SpeakingRate = 1f;
        public float EmotionalIntensity = 1f;
        public float TempoDynamics = 1f;
        public float Pitch = 0f;
        public float Volume = 1f;
        public float LeadingSilenceSeconds = 0.0f;
        public float TrailingSilenceSeconds = 0.3f;
        public float LineBreakSilenceSeconds = 0.3f;
        public string OutputFormat = "wav";
        public int OutputBitrate = 0;
        public int OutputSamplingRate = 16000;
        public string OutputAudioChannels = "mono";
        public List<VoiceStyle> VoiceStyles;

        private ChatdollHttp client;

        private void Start()
        {
            client = new ChatdollHttp(Timeout);
        }

        // Get audio clip from Aivis Cloud API
        // https://api.aivis-project.com/v1/docs#tag/Text-to-Speech/operation/TextToSpeechAPI
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; }
            ;

            var textToSpeech = text.Replace(" ", "").Replace("\n", "").Trim();
            if (string.IsNullOrEmpty(textToSpeech) || textToSpeech == "」") return null;

            // Apply style
            var inlineStyle = StyleName;
            if (parameters.ContainsKey("style"))
            {
                var voiceStyle = parameters["style"] as string;
                if (!string.IsNullOrEmpty(voiceStyle))
                {
                    foreach (var style in VoiceStyles)
                    {
                        if (style.VoiceStyleValue == voiceStyle)
                        {
                            inlineStyle = style.AivisCloudVoiceStyleName;
                            break;
                        }
                    }
                }
            }

            var url = string.IsNullOrEmpty(EndpointUrl)
                ? "https://api.aivis-project.com/v1/tts/synthesize"
                : EndpointUrl;

            var headers = new Dictionary<string, string>()
            {
                { "Authorization", $"Bearer {ApiKey}"}
            };

            var payload = new Dictionary<string, object>()
            {
                {"text", textToSpeech},
                {"model_uuid", ModelUUID},
                {"style_id", StyleId},
                {"use_ssml", UseSSML},
                {"language", Language},
                {"speaking_rate", SpeakingRate},
                {"emotional_intensity", EmotionalIntensity},
                {"tempo_dynamics", TempoDynamics},
                {"pitch", Pitch},
                {"volume", Volume},
                {"leading_silence_seconds", LeadingSilenceSeconds},
                {"trailing_silence_seconds", TrailingSilenceSeconds},
                {"line_break_silence_seconds", LineBreakSilenceSeconds},
                {"output_format", OutputFormat},
                {"output_sampling_rate", OutputSamplingRate},
                {"output_audio_channels", OutputAudioChannels}
            };

            if (!string.IsNullOrEmpty(SpeakerUUID))
            {
                payload["speaker_uuid"] = SpeakerUUID;
            }
            if (!string.IsNullOrEmpty(inlineStyle))
            {
                payload["style_name"] = inlineStyle;
            }
            if (!string.IsNullOrEmpty(UserDictionaryUUID))
            {
                payload["user_dictionary_uuid"] = UserDictionaryUUID;
            }
            if (OutputBitrate > 0)
            {
                payload["output_bitrate"] = OutputBitrate;
            }

            var audioResp = await client.PostJsonAsync(url, payload, headers, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(audioResp.Data, searchDataChunk: true);    // Size is not set in response
        }

        [Serializable]
        public class VoiceStyle
        {
            public string VoiceStyleValue;
            public string AivisCloudVoiceStyleName;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
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
    public class StyleBertVits2SpeechSynthesizer : SpeechSynthesizerBase
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
        public string ModelName = "amitaro";
        public int ModelId = 0;
        public string SpeakerName = "あみたろ";
        public int SpeakerId = 0;
        public string Style = "Neutral";
        public float StyleWeight = 1.0f;
        public float SdpRatio = 0.2f;
        public float Noise = 0.6f;
        public float NoiseW = 0.8f;
        public float Length = 1.0f;
        public string Language = "JP";
        public bool AutoSplit = true;
        public float SplitInterval = 0.5f;   
        public string AssistText;
        public float AssistTextWeight;
        public string ReferenceAudioPath;
        public List<VoiceStyle> VoiceStyles;

#if UNITY_WEBGL && !UNITY_EDITOR
        private ChatdollHttp client;

        private void Start()
        {
            client = new ChatdollHttp(Timeout);
        }
#endif

        // Get audio clip from StyleBertVits2 API
        // https://github.com/litagin02/Style-Bert-VITS2
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            var textToSpeech = text.Replace(" ", "").Replace("\n", "").Trim();
            if (string.IsNullOrEmpty(textToSpeech) || textToSpeech == "」") return null;

            // Apply style
            var inlineStyle = Style;
            if (parameters.ContainsKey("style"))
            {
                var voiceStyle = parameters["style"] as string;
                if (!string.IsNullOrEmpty(voiceStyle))
                {
                    foreach (var style in VoiceStyles)
                    {
                        if (style.VoiceStyleValue == voiceStyle)
                        {
                            inlineStyle = style.StyleBertVITSStyle;
                            break;
                        }
                    }
                }
            }

            // Make query
            var url = EndpointUrl + $"/voice?text={UnityWebRequest.EscapeURL(textToSpeech, Encoding.UTF8)}";
            if (!string.IsNullOrEmpty(ModelName)) url += $"&model_name={UnityWebRequest.EscapeURL(ModelName, Encoding.UTF8)}";
            url += $"&model_id={ModelId}";
            if (!string.IsNullOrEmpty(SpeakerName)) url += $"&speaker_name={UnityWebRequest.EscapeURL(SpeakerName, Encoding.UTF8)}";
            url += $"&sdp_ratio={SdpRatio}";
            url += $"&noise={Noise}";
            url += $"&noisew={NoiseW}";
            url += $"&length={Length}";
            url += $"&language={Language}";
            url += $"&auto_split={AutoSplit}";
            url += $"&split_interval={SplitInterval}";
            if (!string.IsNullOrEmpty(AssistText))
            {
                url += $"&assist_text={AssistText}";
                url += $"&assist_text_weight={AssistTextWeight}";
            }
            url += $"&style={UnityWebRequest.EscapeURL(inlineStyle, Encoding.UTF8)}";
            url += $"&style_weight={StyleWeight}";
            if (!string.IsNullOrEmpty(ReferenceAudioPath))
            {
                url += $"&reference_audio_path={ReferenceAudioPath}";
            }

            return await DownloadAudioClipAsync(url, token);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        protected async UniTask<AudioClip> DownloadAudioClipAsync(string url, CancellationToken token)
        {
            var audioResp = await client.GetAsync(url, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(audioResp.Data);
        }
#else
        protected async UniTask<AudioClip> DownloadAudioClipAsync(string url, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                www.timeout = Timeout;
                www.method = "GET";

                try
                {
                    await www.SendWebRequest().ToUniTask(cancellationToken: token);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occured while processing StyleBertVits2 text-to-speech: {ex}");
                    return null;
                }

                return DownloadHandlerAudioClip.GetContent(www);
            }
        }
#endif

        [Serializable]
        public class VoiceStyle
        {
            public string VoiceStyleValue;
            public string StyleBertVITSStyle;
        }
    }
}

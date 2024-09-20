using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.StyleBertVits2
{
    public class StyleBertVits2TTSLoader : WebVoiceLoaderBase
    {
        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "StyleBertVits2";
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

        public void Configure(string endpointUrl, bool overwrite = false)
        {
            EndpointUrl = string.IsNullOrEmpty(EndpointUrl) || overwrite ? endpointUrl : EndpointUrl;
        }

        // Get audio clip from StyleBertVits2 API
        // https://github.com/litagin02/Style-Bert-VITS2
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            var textToSpeech = voice.Text.Replace(" ", "").Replace("\n", "").Trim();
            if (string.IsNullOrEmpty(textToSpeech) || textToSpeech == "」") return null;

            // Apply style
            var voiceStyle = voice.GetTTSParam("style") as string;
            var inlineStyle = Style;
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

#if UNITY_WEBGL && !UNITY_EDITOR
            return await DownloadAudioClipWebGLAsync(url, token);
#else
            return await DownloadAudioClipNativeAsync(url, token);
#endif
        }

        protected async UniTask<AudioClip> DownloadAudioClipNativeAsync(string url, CancellationToken token)
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

        protected async UniTask<AudioClip> DownloadAudioClipWebGLAsync(string url, CancellationToken token)
        {
            var audioResp = await client.GetAsync(url, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(audioResp.Data);
        }

        [Serializable]
        public class VoiceStyle
        {
            public string VoiceStyleValue;
            public string StyleBertVITSStyle;
        }
    }
}

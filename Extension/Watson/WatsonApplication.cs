using UnityEngine;

namespace ChatdollKit.Extension.Watson
{
    [RequireComponent(typeof(WatsonWakeWordListener))]
    [RequireComponent(typeof(WatsonVoiceRequestProvider))]
    [RequireComponent(typeof(WatsonTTSLoader))]
    public class WatsonApplication : ChatdollApplication
    {
        [Header("Watson Speach-to-Text Service")]
        public string STTApiKey;
        public string STTBaseUrl;
        public string STTModel;
        public bool STTRemoveWordSeparation;

        [Header("Watson Text-to-Speach Service")]
        public string TTSApiKey;
        public string TTSBaseUrl;
        public string TTSSpeakerName;

        protected override void Awake()
        {
            Configure(gameObject, STTApiKey, STTBaseUrl, STTModel, STTRemoveWordSeparation, TTSApiKey, TTSBaseUrl, TTSSpeakerName);
            base.Awake();
        }

        public static void Configure(GameObject gameObject, string sttApiKey, string sttBaseUrl, string sttModel, bool sttRemoveWordSeparation, string ttsApiKey, string ttsBaseUrl, string ttsSpeakerName)
        {
            // Set API key and some properties to each component
            var wakewordListener = gameObject.GetComponent<WatsonWakeWordListener>();
            if (wakewordListener != null)
            {
                wakewordListener.ApiKey = string.IsNullOrEmpty(wakewordListener.ApiKey) ? sttApiKey : wakewordListener.ApiKey;
                wakewordListener.BaseUrl = string.IsNullOrEmpty(wakewordListener.BaseUrl) ? sttBaseUrl : wakewordListener.BaseUrl;
                wakewordListener.Model = string.IsNullOrEmpty(wakewordListener.Model) ? sttModel : wakewordListener.Model;
                wakewordListener.RemoveWordSeparation = sttRemoveWordSeparation;
            }

            var voiceRequestProvider = gameObject.GetComponent<WatsonVoiceRequestProvider>();
            if (voiceRequestProvider != null)
            {
                voiceRequestProvider.ApiKey = string.IsNullOrEmpty(voiceRequestProvider.ApiKey) ? sttApiKey : voiceRequestProvider.ApiKey;
                voiceRequestProvider.BaseUrl = string.IsNullOrEmpty(voiceRequestProvider.BaseUrl) ? sttBaseUrl : voiceRequestProvider.BaseUrl;
                voiceRequestProvider.Model = string.IsNullOrEmpty(voiceRequestProvider.Model) ? sttModel : voiceRequestProvider.Model;
                voiceRequestProvider.RemoveWordSeparation = sttRemoveWordSeparation;
            }

            var ttsLoader = gameObject.GetComponent<WatsonTTSLoader>();
            if (ttsLoader != null)
            {
                ttsLoader.ApiKey = string.IsNullOrEmpty(ttsLoader.ApiKey) ? ttsApiKey : ttsLoader.ApiKey;
                ttsLoader.BaseUrl = string.IsNullOrEmpty(ttsLoader.BaseUrl) ? ttsBaseUrl : ttsLoader.BaseUrl;
                ttsLoader.SpeakerName = string.IsNullOrEmpty(ttsLoader.SpeakerName) ? ttsSpeakerName : ttsLoader.SpeakerName;
            }
        }
    }
}

using UnityEngine;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Extension.Gatebox
{
    [RequireComponent(typeof(WatsonWakeWordListener))]
    [RequireComponent(typeof(WatsonVoiceRequestProvider))]
    [RequireComponent(typeof(WatsonTTSLoader))]
    public class GateboxApplicationWatson : GateboxApplication
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

        protected override void OnComponentsReady(ScriptableObject config)
        {
            if (config != null)
            {
                var appConfig = (WatsonApplicationConfig)config;
                STTApiKey = appConfig.STTApiKey;
                STTBaseUrl = appConfig.STTBaseUrl;
                STTModel = appConfig.STTModel;
                STTRemoveWordSeparation = appConfig.STTRemoveWordSeparation;
                TTSApiKey = appConfig.TTSApiKey;
                TTSBaseUrl = appConfig.TTSBaseUrl;
                TTSSpeakerName = appConfig.TTSSpeakerName;
            }

            // Set API key and language to each component
            var ww = wakeWordListener as WatsonWakeWordListener;
            if (ww != null)
            {
                ww.ApiKey = string.IsNullOrEmpty(ww.ApiKey) ? STTApiKey : ww.ApiKey;
                ww.BaseUrl = string.IsNullOrEmpty(ww.BaseUrl) ? STTBaseUrl : ww.BaseUrl;
                ww.Model = string.IsNullOrEmpty(ww.Model) ? STTModel : ww.Model;
                ww.RemoveWordSeparation = ww.RemoveWordSeparation ? STTRemoveWordSeparation : ww.RemoveWordSeparation;
            }

            var vreq = voiceRequestProvider as WatsonVoiceRequestProvider;
            if (vreq != null)
            {
                vreq.ApiKey = string.IsNullOrEmpty(vreq.ApiKey) ? STTApiKey : vreq.ApiKey;
                vreq.BaseUrl = string.IsNullOrEmpty(vreq.BaseUrl) ? STTBaseUrl : vreq.BaseUrl;
                vreq.Model = string.IsNullOrEmpty(vreq.Model) ? STTModel : vreq.Model;
                vreq.RemoveWordSeparation = vreq.RemoveWordSeparation ? STTRemoveWordSeparation : vreq.RemoveWordSeparation;
            }

            var ttsLoader = gameObject.GetComponent<WatsonTTSLoader>();
            if (ttsLoader != null)
            {
                ttsLoader.ApiKey = string.IsNullOrEmpty(ttsLoader.ApiKey) ? TTSApiKey : ttsLoader.ApiKey;
                ttsLoader.BaseUrl = string.IsNullOrEmpty(ttsLoader.BaseUrl) ? TTSBaseUrl : ttsLoader.BaseUrl;
                ttsLoader.SpeakerName = string.IsNullOrEmpty(ttsLoader.SpeakerName) ? TTSSpeakerName : ttsLoader.SpeakerName;
            }
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = (WatsonApplicationConfig)base.CreateConfig(
                config ?? ScriptableObject.CreateInstance<WatsonApplicationConfig>()
            );

            appConfig.STTApiKey = STTApiKey;
            appConfig.STTBaseUrl = STTBaseUrl;
            appConfig.STTModel = STTModel;
            appConfig.STTRemoveWordSeparation = STTRemoveWordSeparation;
            appConfig.TTSApiKey = TTSApiKey;
            appConfig.TTSBaseUrl = TTSBaseUrl;
            appConfig.TTSSpeakerName = TTSSpeakerName;

            return appConfig;
        }
    }
}

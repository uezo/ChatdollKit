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

        protected override void OnComponentsReady(ScriptableObject config)
        {
            // Apply configuraton to this app and its components
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

            (wakeWordListener as WatsonWakeWordListener)?.Configure(STTApiKey, STTModel, STTBaseUrl, STTRemoveWordSeparation);
            (voiceRequestProvider as WatsonVoiceRequestProvider)?.Configure(STTApiKey, STTModel, STTBaseUrl, STTRemoveWordSeparation);
            (gameObject.GetComponent<WatsonTTSLoader>())?.Configure(TTSApiKey, TTSBaseUrl, TTSSpeakerName);
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

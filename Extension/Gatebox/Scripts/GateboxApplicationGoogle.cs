using UnityEngine;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Extension.Gatebox
{
    [RequireComponent(typeof(GoogleWakeWordListener))]
    [RequireComponent(typeof(GoogleVoiceRequestProvider))]
    [RequireComponent(typeof(GoogleTTSLoader))]
    public class GateboxApplicationGoogle : GateboxApplication
    {
        [Header("Google Cloud Speech API")]
        public string ApiKey;
        public string Language;

        protected override void OnComponentsReady(ScriptableObject config)
        {
            if (config != null)
            {
                var appConfig = (GoogleApplicationConfig)config;
                ApiKey = appConfig.SpeechApiKey;
                Language = appConfig.Language;
            }

            // Set API key and language to each component
            var ww = wakeWordListener as GoogleWakeWordListener;
            if (ww != null)
            {
                ww.ApiKey = string.IsNullOrEmpty(ww.ApiKey) ? ApiKey : ww.ApiKey;
                ww.Language = string.IsNullOrEmpty(ww.Language) ? Language : ww.Language;
            }

            var vreq = voiceRequestProvider as GoogleVoiceRequestProvider;
            if (vreq != null)
            {
                vreq.ApiKey = string.IsNullOrEmpty(vreq.ApiKey) ? ApiKey : vreq.ApiKey;
                vreq.Language = string.IsNullOrEmpty(vreq.Language) ? Language : vreq.Language;
            }

            var ttsLoader = gameObject.GetComponent<GoogleTTSLoader>();
            if (ttsLoader != null)
            {
                ttsLoader.ApiKey = string.IsNullOrEmpty(ttsLoader.ApiKey) ? ApiKey : ttsLoader.ApiKey;
                ttsLoader.Language = string.IsNullOrEmpty(ttsLoader.Language) ? Language : ttsLoader.Language;
            }
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = (GoogleApplicationConfig)base.CreateConfig(
                config ?? ScriptableObject.CreateInstance<GoogleApplicationConfig>()
            );

            appConfig.SpeechApiKey = ApiKey;
            appConfig.Language = Language;
            appConfig.Gender = "FEMALE";
            appConfig.SpeakerName = "ja-JP-Standard-A";

            return appConfig;
        }
    }
}

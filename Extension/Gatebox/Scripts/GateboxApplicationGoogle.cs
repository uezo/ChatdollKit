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
        public string Gender = "FEMALE";
        public string SpeakerName = "ja-JP-Standard-A";

        protected override void OnComponentsReady(ScriptableObject config)
        {
            if (config != null)
            {
                var appConfig = (GoogleApplicationConfig)config;
                ApiKey = appConfig.SpeechApiKey;
                Language = appConfig.Language;
            }

            (wakeWordListener as GoogleWakeWordListener)?.Configure(ApiKey, Language);
            (voiceRequestProvider as GoogleVoiceRequestProvider)?.Configure(ApiKey, Language);
            (gameObject.GetComponent<GoogleTTSLoader>())?.Configure(ApiKey, Language, Gender, SpeakerName);
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = (GoogleApplicationConfig)base.CreateConfig(
                config ?? ScriptableObject.CreateInstance<GoogleApplicationConfig>()
            );

            appConfig.SpeechApiKey = ApiKey;
            appConfig.Language = Language;
            appConfig.Gender = Gender;
            appConfig.SpeakerName = SpeakerName;

            return appConfig;
        }
    }
}

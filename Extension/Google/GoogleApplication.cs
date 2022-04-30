using UnityEngine;

namespace ChatdollKit.Extension.Google
{
    [RequireComponent(typeof(GoogleWakeWordListener))]
    [RequireComponent(typeof(GoogleVoiceRequestProvider))]
    [RequireComponent(typeof(GoogleTTSLoader))]
    public class GoogleApplication : ChatdollKit
    {
        [Header("Google Cloud Speech API")]
        public string ApiKey;
        public string Language;
        public string Gender = "FEMALE";
        public string SpeakerName = "ja-JP-Standard-A";

        protected override void OnComponentsReady()
        {
            GetComponent<GoogleWakeWordListener>().Configure(ApiKey, Language);
            GetComponent<GoogleVoiceRequestProvider>().Configure(ApiKey, Language);
            GetComponent<GoogleTTSLoader>().Configure(ApiKey, Language, Gender, SpeakerName);
        }

        public override ScriptableObject LoadConfig()
        {
            var config = base.LoadConfig();

            if (config != null)
            {
                var appConfig = (GoogleApplicationConfig)config;
                ApiKey = appConfig.SpeechApiKey;
                Language = appConfig.Language;
            }

            return config;
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = config == null ? GoogleApplicationConfig.CreateInstance<GoogleApplicationConfig>() : (GoogleApplicationConfig)config;

            appConfig.SpeechApiKey = ApiKey;
            appConfig.Language = Language;
            appConfig.Gender = Gender;
            appConfig.SpeakerName = SpeakerName;

            base.CreateConfig(appConfig);

            return appConfig;
        }
    }
}

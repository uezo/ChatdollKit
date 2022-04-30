using UnityEngine;

namespace ChatdollKit.Extension.Azure
{
    [RequireComponent(typeof(AzureWakeWordListener))]
    [RequireComponent(typeof(AzureVoiceRequestProvider))]
    [RequireComponent(typeof(AzureTTSLoader))]
    public class AzureApplication : ChatdollApplication
    {
        [Header("Azure Speech Services")]
        public string ApiKey = string.Empty;
        public string Region = string.Empty;
        public string Language = string.Empty;
        public string Gender = "Female";
        public string SpeakerName = "ja-JP-HarukaRUS";

        [Header("Remote Log")]
        public string LogTableUri;

        protected override void OnComponentsReady(ScriptableObject config)
        {
            // Apply configuraton to this app and its components
            if (config != null)
            {
                var appConfig = (AzureApplicationConfig)config;
                LogTableUri = appConfig.LogTableUri;
                ApiKey = appConfig.SpeechApiKey;
                Region = appConfig.Region;
                Language = appConfig.Language;
                Gender = appConfig.Gender;
                SpeakerName = appConfig.SpeakerName;
            }

            // Remote log
            if (!string.IsNullOrEmpty(LogTableUri))
            {
                Debug.unityLogger.filterLogType = LogType.Warning;
                var azureHandler = new AzureTableStorageHandler(LogTableUri, LogType.Warning);
                Application.logMessageReceived += azureHandler.HandleLog;
            }

            (wakeWordListener as AzureWakeWordListener)?.Configure(ApiKey, Language, Region);
            (voiceRequestProvider as AzureVoiceRequestProvider)?.Configure(ApiKey, Language, Region);
            (gameObject.GetComponent<AzureTTSLoader>())?.Configure(ApiKey, Language, Gender, SpeakerName, Region);
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = (AzureApplicationConfig)base.CreateConfig(
                config ?? ScriptableObject.CreateInstance<AzureApplicationConfig>()
            );

            appConfig.LogTableUri = LogTableUri;
            appConfig.SpeechApiKey = ApiKey;
            appConfig.Region = Region;
            appConfig.Language = Language;
            appConfig.Gender = Gender;
            appConfig.SpeakerName = SpeakerName;

            return appConfig;
        }
    }
}

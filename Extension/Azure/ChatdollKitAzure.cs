using UnityEngine;

namespace ChatdollKit.Extension.Azure
{
    [RequireComponent(typeof(AzureWakeWordListener))]
    [RequireComponent(typeof(AzureVoiceRequestProvider))]
    [RequireComponent(typeof(AzureTTSLoader))]
    public class ChatdollKitAzure : ChatdollKit
    {
        [Header("Azure Speech Services")]
        public string ApiKey = string.Empty;
        public string Region = string.Empty;
        public string Language = string.Empty;
        public string Gender = "Female";
        public string SpeakerName = "ja-JP-HarukaRUS";

        [Header("Remote Log")]
        public string LogTableUri;

        protected override void OnComponentsReady()
        {
            base.OnComponentsReady();

            // Remote log
            if (!string.IsNullOrEmpty(LogTableUri))
            {
                Debug.unityLogger.filterLogType = LogType.Warning;
                var azureHandler = new AzureTableStorageHandler(LogTableUri, LogType.Warning);
                Application.logMessageReceived += azureHandler.HandleLog;
            }

            GetComponent<AzureWakeWordListener>().Configure(ApiKey, Language, Region);
            GetComponent<AzureVoiceRequestProvider>().Configure(ApiKey, Language, Region);
            GetComponent<AzureTTSLoader>().Configure(ApiKey, Language, Gender, SpeakerName, Region);
        }

        public override ScriptableObject LoadConfig()
        {
            var config = base.LoadConfig();

            if (config != null)
            {
                var appConfig = (ChatdollKitAzureConfig)config;
                LogTableUri = appConfig.LogTableUri;
                ApiKey = appConfig.SpeechApiKey;
                Region = appConfig.Region;
                Language = appConfig.Language;
                Gender = appConfig.Gender;
                SpeakerName = appConfig.SpeakerName;
            }

            return config;
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = config == null ? ChatdollKitAzureConfig.CreateInstance<ChatdollKitAzureConfig>() : (ChatdollKitAzureConfig)config;

            appConfig.LogTableUri = LogTableUri;
            appConfig.SpeechApiKey = ApiKey;
            appConfig.Region = Region;
            appConfig.Language = Language;
            appConfig.Gender = Gender;
            appConfig.SpeakerName = SpeakerName;

            base.CreateConfig(appConfig);

            return appConfig;
        }
    }
}

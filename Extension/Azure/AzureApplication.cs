using UnityEngine;

namespace ChatdollKit.Extension.Azure
{
    [RequireComponent(typeof(AzureWakeWordListener))]
    [RequireComponent(typeof(AzureVoiceRequestProvider))]
    [RequireComponent(typeof(AzureTTSLoader))]
    public class AzureApplication : ChatdollApplication
    {
        [Header("Azure Speech Services")]
        public string ApiKey;
        public string Region;
        public string Language;

        [Header("Remote Log")]
        public string LogTableUri;

        protected override void Awake()
        {
            Configure(gameObject, ApiKey, Region, Language, LogTableUri);
            base.Awake();
        }

        public static void Configure(GameObject gameObject, string apiKey, string region, string language, string logTableUri = null) 
        {
            // Remote log
            if (!string.IsNullOrEmpty(logTableUri))
            {
                Debug.unityLogger.filterLogType = LogType.Warning;
                var azureHandler = new AzureTableStorageHandler(logTableUri, LogType.Warning);
                Application.logMessageReceived += azureHandler.HandleLog;
            }

            // Set API key, region and language to each component
            var wakewordListener = gameObject.GetComponent<AzureWakeWordListener>();
            if (wakewordListener != null)
            {
                wakewordListener.ApiKey = string.IsNullOrEmpty(wakewordListener.ApiKey) ? apiKey : wakewordListener.ApiKey;
                wakewordListener.Region = string.IsNullOrEmpty(wakewordListener.Region) ? region : wakewordListener.Region;
                wakewordListener.Language = string.IsNullOrEmpty(wakewordListener.Language) ? language : wakewordListener.Language;
            }

            var voiceRequestProvider = gameObject.GetComponent<AzureVoiceRequestProvider>();
            if (voiceRequestProvider != null)
            {
                voiceRequestProvider.ApiKey = string.IsNullOrEmpty(voiceRequestProvider.ApiKey) ? apiKey : voiceRequestProvider.ApiKey;
                voiceRequestProvider.Region = string.IsNullOrEmpty(voiceRequestProvider.Region) ? region : voiceRequestProvider.Region;
                voiceRequestProvider.Language = string.IsNullOrEmpty(voiceRequestProvider.Language) ? language : voiceRequestProvider.Language;
            }

            var ttsLoader = gameObject.GetComponent<AzureTTSLoader>();
            if (ttsLoader != null)
            {
                ttsLoader.ApiKey = string.IsNullOrEmpty(ttsLoader.ApiKey) ? apiKey : ttsLoader.ApiKey;
                ttsLoader.Region = string.IsNullOrEmpty(ttsLoader.Region) ? region : ttsLoader.Region;
                ttsLoader.Language = string.IsNullOrEmpty(ttsLoader.Language) ? language : ttsLoader.Language;
            }
        }
    }
}

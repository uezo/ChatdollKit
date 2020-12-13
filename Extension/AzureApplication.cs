using UnityEngine;

namespace ChatdollKit.Extension
{
    [RequireComponent(typeof(AzureWakeWordListener))]
    [RequireComponent(typeof(AzureVoiceRequestProvider))]
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
            // Remote log
            if (!string.IsNullOrEmpty(LogTableUri))
            {
                Debug.unityLogger.filterLogType = LogType.Warning;
                var azureHandler = new AzureTableStorageHandler(LogTableUri, LogType.Warning);
                Application.logMessageReceived += azureHandler.HandleLog;
            }

            // Set API key and region to each component
            var wakewordListener = gameObject.GetComponent<AzureWakeWordListener>();
            wakewordListener.ApiKey = string.IsNullOrEmpty(wakewordListener.ApiKey) ? ApiKey : wakewordListener.ApiKey;
            wakewordListener.Region = string.IsNullOrEmpty(wakewordListener.Region) ? Region : wakewordListener.Region;
            wakewordListener.Language = string.IsNullOrEmpty(wakewordListener.Language) ? Language : wakewordListener.Language;

            var voiceRequestProvider = gameObject.GetComponent<AzureVoiceRequestProvider>();
            voiceRequestProvider.ApiKey = string.IsNullOrEmpty(voiceRequestProvider.ApiKey) ? ApiKey : voiceRequestProvider.ApiKey;
            voiceRequestProvider.Region = string.IsNullOrEmpty(voiceRequestProvider.Region) ? Region : voiceRequestProvider.Region;
            voiceRequestProvider.Language = string.IsNullOrEmpty(voiceRequestProvider.Language) ? Language : voiceRequestProvider.Language;

            var ttsLoader = gameObject.GetComponent<AzureTTSLoader>();
            if (ttsLoader != null)
            {
                ttsLoader.ApiKey = string.IsNullOrEmpty(ttsLoader.ApiKey) ? ApiKey : ttsLoader.ApiKey;
                ttsLoader.Region = string.IsNullOrEmpty(ttsLoader.Region) ? Region : ttsLoader.Region;
                ttsLoader.Language = string.IsNullOrEmpty(ttsLoader.Language) ? Language : ttsLoader.Language;
            }

            base.Awake();
        }
    }
}

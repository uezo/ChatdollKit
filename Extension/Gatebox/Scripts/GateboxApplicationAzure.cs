using UnityEngine;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Extension.Gatebox
{
    [RequireComponent(typeof(AzureWakeWordListener))]
    [RequireComponent(typeof(AzureVoiceRequestProvider))]
    [RequireComponent(typeof(AzureTTSLoader))]
    public class GateboxApplicationAzure : GateboxApplication
    {
        [Header("Azure Speech Services")]
        public string ApiKey;
        public string Region;
        public string Language;

        [Header("Remote Log")]
        public string LogTableUri;

        protected override void OnComponentsReady(ScriptableObject config)
        {
            if (config != null)
            {
                var appConfig = (AzureApplicationConfig)config;
                LogTableUri = appConfig.LogTableUri;
                ApiKey = appConfig.SpeechApiKey;
                Region = appConfig.Region;
                Language = appConfig.Language;
            }

            // Remote log
            if (!string.IsNullOrEmpty(LogTableUri))
            {
                Debug.unityLogger.filterLogType = LogType.Warning;
                var azureHandler = new AzureTableStorageHandler(LogTableUri, LogType.Warning);
                Application.logMessageReceived += azureHandler.HandleLog;
            }

            // Set API key and language to each component
            var ww = wakeWordListener as AzureWakeWordListener;
            if (ww != null)
            {
                ww.ApiKey = string.IsNullOrEmpty(ww.ApiKey) ? ApiKey : ww.ApiKey;
                ww.Region = string.IsNullOrEmpty(ww.Region) ? Region : ww.Region;
                ww.Language = string.IsNullOrEmpty(ww.Language) ? Language : ww.Language;
            }

            var vreq = voiceRequestProvider as AzureVoiceRequestProvider;
            if (vreq != null)
            {
                vreq.ApiKey = string.IsNullOrEmpty(vreq.ApiKey) ? ApiKey : vreq.ApiKey;
                vreq.Region = string.IsNullOrEmpty(vreq.Region) ? Region : vreq.Region;
                vreq.Language = string.IsNullOrEmpty(vreq.Language) ? Language : vreq.Language;
            }

            var ttsLoader = gameObject.GetComponent<AzureTTSLoader>();
            if (ttsLoader != null)
            {
                ttsLoader.ApiKey = string.IsNullOrEmpty(ttsLoader.ApiKey) ? ApiKey : ttsLoader.ApiKey;
                ttsLoader.Region = string.IsNullOrEmpty(ttsLoader.Region) ? Region : ttsLoader.Region;
                ttsLoader.Language = string.IsNullOrEmpty(ttsLoader.Language) ? Language : ttsLoader.Language;
            }
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

            return appConfig;
        }
    }
}

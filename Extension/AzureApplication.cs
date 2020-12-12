using UnityEngine;

namespace ChatdollKit.Extension
{
    [RequireComponent(typeof(AzureWakeWordListener))]
    [RequireComponent(typeof(AzureVoiceRequestProvider))]
    public class AzureApplication : ChatdollApplication
    {
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

            base.Awake();
        }
    }
}

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

        protected override void Awake()
        {
            AzureApplication.Configure(gameObject, ApiKey, Region, Language, LogTableUri);
            base.Awake();
        }
    }
}

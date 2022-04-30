using UnityEngine;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.SkillServer
{
    [RequireComponent(typeof(RemoteRequestProcessor))]
    public class MainAzure : AzureApplication
    {
        [Header("Remote Request Processor")]
        public string BaseUrl = string.Empty;

        protected override void OnComponentsReady()
        {
            base.OnComponentsReady();

            GetComponent<RemoteRequestProcessor>().BaseUrl = BaseUrl;
        }

        public override ScriptableObject LoadConfig()
        {
            var config = base.LoadConfig();

            if (config != null)
            {
                BaseUrl = ((AzureRemoteApplicationConfig)config).BaseUrl;
            }

            return config;
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = config == null ? AzureRemoteApplicationConfig.CreateInstance<AzureRemoteApplicationConfig>() : (AzureRemoteApplicationConfig)config;

            appConfig.BaseUrl = BaseUrl;

            base.CreateConfig(appConfig);

            return appConfig;
        }
    }
}

using UnityEngine;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Examples.SkillServer
{
    [RequireComponent(typeof(RemoteRequestProcessor))]
    public class MainWatson : WatsonApplication
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
                BaseUrl = ((WatsonRemoteApplicationConfig)config).BaseUrl;
            }

            return config;
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = config == null ? WatsonRemoteApplicationConfig.CreateInstance<WatsonRemoteApplicationConfig>() : (WatsonRemoteApplicationConfig)config;

            appConfig.BaseUrl = BaseUrl;

            base.CreateConfig(appConfig);

            return appConfig;
        }
    }
}

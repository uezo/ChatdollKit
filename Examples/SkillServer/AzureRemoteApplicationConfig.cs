using UnityEngine;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.SkillServer
{
    public class AzureRemoteApplicationConfig : ChatdollKitAzureConfig
    {
        [Header("Remote Url")]
        public string BaseUrl;
    }
}

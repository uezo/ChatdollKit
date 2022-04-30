using UnityEngine;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Examples.SkillServer
{
    public class WatsonRemoteApplicationConfig : WatsonApplicationConfig
    {
        [Header("Remote Url")]
        public string BaseUrl;
    }
}

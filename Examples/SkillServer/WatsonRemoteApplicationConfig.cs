using UnityEngine;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Examples.SkillServer
{
    public class WatsonRemoteApplicationConfig : ChatdollKitWatsonConfig
    {
        [Header("Remote Url")]
        public string BaseUrl;
    }
}

using UnityEngine;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Examples.SkillServer
{
    public class GoogleRemoteApplicationConfig : ChatdollKitGoogleConfig
    {
        [Header("Remote Url")]
        public string BaseUrl;
    }
}

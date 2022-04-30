using UnityEngine;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Examples.SkillServer
{
    public class GoogleRemoteApplicationConfig : GoogleApplicationConfig
    {
        [Header("Remote Url")]
        public string BaseUrl;
    }
}

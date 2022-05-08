using UnityEngine;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.Examples.SkillServer
{
    [RequireComponent(typeof(RemoteRequestProcessor))]
    public class ChatdollKitRemote : ChatdollKit
    {
        [Header("Remote Request Processor")]
        public string BaseUrl = string.Empty;

        public override ChatdollKitConfig LoadConfig()
        {
            return ChatdollKitRemoteConfig.Load(this, ApplicationName);
        }

        public override ChatdollKitConfig CreateConfig()
        {
            return ChatdollKitRemoteConfig.Create(this);
        }
    }
}

using UnityEngine;

namespace ChatdollKit.Examples.SkillServer
{
    public class ChatdollKitRemoteConfig : ChatdollKitConfig
    {
        [Header("Remote Url")]
        public string BaseUrl;

        public new static ChatdollKitConfig Create(ChatdollKit chatdollKit, ChatdollKitConfig extendedConfig = null)
        {
            var config = extendedConfig == null ? ChatdollKitRemoteConfig.CreateInstance<ChatdollKitRemoteConfig>() : (ChatdollKitRemoteConfig)extendedConfig;

            config.BaseUrl = ((ChatdollKitRemote)chatdollKit).BaseUrl;

            ChatdollKitConfig.Create(chatdollKit, config);

            return config;
        }

        public new static ChatdollKitConfig Load(ChatdollKit chatdollKit, string applicationName, ChatdollKitConfig extendedConfig = null)
        {
            var config = extendedConfig == null ? Resources.Load<ChatdollKitRemoteConfig>(applicationName) : (ChatdollKitRemoteConfig)extendedConfig;

            // configに値が存在しない時のnullcheckが必要

            ((ChatdollKitRemote)chatdollKit).BaseUrl = config.BaseUrl;

            ChatdollKitConfig.Load(chatdollKit, applicationName, config);

            return config;
        }
    }
}

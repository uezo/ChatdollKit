using UnityEngine;

namespace ChatdollKit.Examples.MultiSkills
{
    public class ChatdollKitMultiSkillsConfig : ChatdollKitConfig
    {
        [Header("Skill settings")]
        public string TranslationApiKey;
        public string TranslationBaseUrl;
        public WeatherSkill.WeatherLocation WeatherLocation = WeatherSkill.WeatherLocation.Tokyo;

        public new static ChatdollKitConfig Create(ChatdollKit chatdollKit, ChatdollKitConfig extendedConfig = null)
        {
            // Create this configuration
            var config = extendedConfig == null ? ChatdollKitMultiSkillsConfig.CreateInstance<ChatdollKitMultiSkillsConfig>() : (ChatdollKitMultiSkillsConfig)extendedConfig;
            var app = (ChatdollKitMultiSkills)chatdollKit;
            config.TranslationApiKey = app.TranslationApiKey;
            config.TranslationBaseUrl = app.TranslationBaseUrl;
            config.WeatherLocation = app.WeatherLocation;

            // Add base configuration
            ChatdollKitConfig.Create(chatdollKit, config);

            return config;
        }

        public new static ChatdollKitConfig Load(ChatdollKit chatdollKit, string applicationName, ChatdollKitConfig extendedConfig = null)
        {
            var baseConfig = extendedConfig == null ? Resources.Load<ChatdollKitConfig>(applicationName) : extendedConfig;
            if (baseConfig == null)
            {
                return null;
            }

            // Load base configuration
            ChatdollKitConfig.Load(chatdollKit, applicationName, baseConfig);

            // Load this configuration
            var config = (ChatdollKitMultiSkillsConfig)baseConfig;
            var app = (ChatdollKitMultiSkills)chatdollKit;
            app.TranslationApiKey = config.TranslationApiKey;
            app.TranslationBaseUrl = config.TranslationBaseUrl;
            app.WeatherLocation = config.WeatherLocation;

            return config;
        }
    }
}

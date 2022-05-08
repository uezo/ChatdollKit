using UnityEngine;

namespace ChatdollKit.Examples.MultiSkills
{
    [RequireComponent(typeof(Router))]
    public class ChatdollKitMultiSkills : ChatdollKit
    {
        [Header("Skill settings")]
        public string TranslationApiKey;
        public string TranslationBaseUrl = "https://api.cognitive.microsofttranslator.com";
        public WeatherSkill.WeatherLocation WeatherLocation = WeatherSkill.WeatherLocation.Tokyo;

        protected override void Awake()
        {
            base.Awake();

            var translationSkill = GetComponent<TranslateSkill>();
            if (translationSkill != null)
            {
                translationSkill.ApiKey = TranslationApiKey;
                translationSkill.BaseUrl = TranslationBaseUrl;
            }

            var weatherSkill = GetComponent<WeatherSkill>();
            if (weatherSkill != null)
            {
                weatherSkill.MyLocation = WeatherLocation;
            }
        }

        public override ChatdollKitConfig LoadConfig()
        {
            return ChatdollKitMultiSkillsConfig.Load(this, ApplicationName);
        }

        public override ChatdollKitConfig CreateConfig()
        {
            return ChatdollKitMultiSkillsConfig.Create(this);
        }
    }
}

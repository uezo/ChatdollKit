using UnityEngine;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Examples.MultiSkills
{
    [RequireComponent(typeof(Router))]
    public class MainWatson : ChatdollKitWatson
    {
        [Header("Skill settings")]
        public string TranslationApiKey;
        public string TranslationBaseUrl;
        public WeatherSkill.WeatherLocation WeatherLocation = WeatherSkill.WeatherLocation.Tokyo;

        protected override void OnComponentsReady()
        {
            base.OnComponentsReady();

            var translationSkill = gameObject.GetComponent<TranslateSkill>();
            translationSkill.ApiKey = TranslationApiKey;
            translationSkill.Engine = TranslateSkill.TranslationEngine.Watson;
            translationSkill.BaseUrl = TranslationBaseUrl;
            gameObject.GetComponent<WeatherSkill>().MyLocation = WeatherLocation;

            DialogController.ChatdollCamera.DecodeCode = QRCodeDecoder.DecodeByZXing;
        }

        public override ScriptableObject LoadConfig()
        {
            var config = base.LoadConfig();

            if (config != null)
            {
                var appConfig = (WatsonMultiSkillConfig)config;
                TranslationApiKey = appConfig.TranslationApiKey;
                TranslationBaseUrl = appConfig.TranslationBaseUrl;
            }

            return config;
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = config == null ? WatsonMultiSkillConfig.CreateInstance<WatsonMultiSkillConfig>() : (WatsonMultiSkillConfig)config;

            appConfig.TranslationApiKey = TranslationApiKey;
            appConfig.TranslationBaseUrl = TranslationBaseUrl;

            base.CreateConfig(appConfig);

            return appConfig;
        }
    }
}

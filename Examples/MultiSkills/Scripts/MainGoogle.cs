using UnityEngine;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Examples.MultiSkills
{
    [RequireComponent(typeof(Router))]
    public class MainGoogle : ChatdollKitGoogle
    {
        [Header("Skill settings")]
        public string TranslationApiKey;
        public string TranslationBaseUrl = "https://translation.googleapis.com/language/translate/v2";
        public WeatherSkill.WeatherLocation WeatherLocation = WeatherSkill.WeatherLocation.Tokyo;

        protected override void OnComponentsReady()
        {
            base.OnComponentsReady();

            var translationSkill = gameObject.GetComponent<TranslateSkill>();
            translationSkill.ApiKey = TranslationApiKey;
            translationSkill.Engine = TranslateSkill.TranslationEngine.Google;
            translationSkill.BaseUrl = TranslationBaseUrl;
            gameObject.GetComponent<WeatherSkill>().MyLocation = WeatherLocation;

            DialogController.ChatdollCamera.DecodeCode = QRCodeDecoder.DecodeByZXing;
        }

        public override ScriptableObject LoadConfig()
        {
            var config = base.LoadConfig();

            if (config != null)
            {
                var appConfig = (GoogleMultiSkillConfig)config;
                TranslationApiKey = appConfig.TranslationApiKey;
                TranslationBaseUrl = appConfig.TranslationBaseUrl;
            }

            return config;
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = config == null ? GoogleMultiSkillConfig.CreateInstance<GoogleMultiSkillConfig>() : (GoogleMultiSkillConfig)config;

            appConfig.TranslationApiKey = TranslationApiKey;
            appConfig.TranslationBaseUrl = TranslationBaseUrl;

            base.CreateConfig(appConfig);

            return appConfig;
        }
    }
}

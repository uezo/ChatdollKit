using UnityEngine;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.MultiSkills
{
    [RequireComponent(typeof(Router))]
    public class MainAzure : AzureApplication
    {
        [Header("Skill settings")]
        public string TranslationApiKey;
        public string TranslationBaseUrl = "https://api.cognitive.microsofttranslator.com";
        public string ChatA3RTApiKey;
        public WeatherSkill.WeatherLocation WeatherLocation = WeatherSkill.WeatherLocation.Tokyo;

        protected override void OnComponentsReady()
        {
            var translationSkill = gameObject.GetComponent<TranslateSkill>();
            translationSkill.ApiKey = TranslationApiKey;
            translationSkill.Engine = TranslateSkill.TranslationEngine.Azure;
            translationSkill.BaseUrl = TranslationBaseUrl;
            gameObject.GetComponent<ChatA3RTSkill>().A3RTApiKey = ChatA3RTApiKey;
            gameObject.GetComponent<WeatherSkill>().MyLocation = WeatherLocation;

            DialogController.ChatdollCamera.DecodeCode = QRCodeDecoder.DecodeByZXing;
        }

        public override ScriptableObject LoadConfig()
        {
            var config = base.LoadConfig();

            if (config != null)
            {
                var appConfig = (AzureMultiSkillConfig)config;
                TranslationApiKey = appConfig.TranslationApiKey;
                TranslationBaseUrl = appConfig.TranslationBaseUrl;
                ChatA3RTApiKey = appConfig.ChatA3RTApiKey;
            }

            return config;
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = config == null ? AzureMultiSkillConfig.CreateInstance<AzureMultiSkillConfig>() : (AzureMultiSkillConfig)config;

            appConfig.TranslationApiKey = TranslationApiKey;
            appConfig.TranslationBaseUrl = TranslationBaseUrl;
            appConfig.ChatA3RTApiKey = ChatA3RTApiKey;

            base.CreateConfig(appConfig);

            return appConfig;
        }
    }
}

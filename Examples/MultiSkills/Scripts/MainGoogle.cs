using UnityEngine;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Examples.MultiSkills
{
    [RequireComponent(typeof(Router))]
    public class MainGoogle : GoogleApplication
    {
        [Header("Skill settings")]
        public string TranslationApiKey;
        public string TranslationBaseUrl = "https://translation.googleapis.com/language/translate/v2";
        public string ChatA3RTApiKey;
        public WeatherSkill.WeatherLocation WeatherLocation = WeatherSkill.WeatherLocation.Tokyo;

        protected override void OnComponentsReady(ScriptableObject config)
        {
            base.OnComponentsReady(config);

            if (config != null)
            {
                var appConfig = (GoogleMultiSkillConfig)config;
                TranslationApiKey = appConfig.TranslationApiKey;
                TranslationBaseUrl = appConfig.TranslationBaseUrl;
                ChatA3RTApiKey = appConfig.ChatA3RTApiKey;
            }

            var translationSkill = gameObject.GetComponent<TranslateSkill>();
            translationSkill.ApiKey = TranslationApiKey;
            translationSkill.Engine = TranslateSkill.TranslationEngine.Google;
            translationSkill.BaseUrl = TranslationBaseUrl;
            gameObject.GetComponent<ChatA3RTSkill>().A3RTApiKey = ChatA3RTApiKey;
            gameObject.GetComponent<WeatherSkill>().MyLocation = WeatherLocation;

            ChatdollCamera.DecodeCode = QRCodeDecoder.DecodeByZXing;
        }

        public override ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            var appConfig = (GoogleMultiSkillConfig)base.CreateConfig(
                config ?? ScriptableObject.CreateInstance<GoogleMultiSkillConfig>()
            );

            appConfig.TranslationApiKey = TranslationApiKey;
            appConfig.TranslationBaseUrl = TranslationBaseUrl;
            appConfig.ChatA3RTApiKey = ChatA3RTApiKey;

            return appConfig;
        }
    }
}

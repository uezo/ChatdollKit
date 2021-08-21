using UnityEngine;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.MultiSkills
{
    [RequireComponent(typeof(Router))]
    public class MainAzure : AzureApplication
    {
        [Header("Skill settings")]
        public string TranslationApiKey;
        public string ChatA3RTApiKey;
        public WeatherSkill.WeatherLocation WeatherLocation = WeatherSkill.WeatherLocation.Tokyo;

        protected override void Awake()
        {
            var translationSkill = gameObject.GetComponent<TranslateSkill>();
            translationSkill.ApiKey = TranslationApiKey;
            translationSkill.Engine = TranslateSkill.TranslationEngine.Azure;
            gameObject.GetComponent<ChatA3RTSkill>().A3RTApiKey = ChatA3RTApiKey;
            gameObject.GetComponent<WeatherSkill>().MyLocation = WeatherLocation;

            base.Awake();
        }
    }
}

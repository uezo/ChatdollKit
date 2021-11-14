using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Examples.MultiSkills
{
    [RequireComponent(typeof(Router))]
    public class MainWatson : WatsonApplication
    {
        [Header("Skill settings")]
        public string TranslationApiKey;
        public string TranslationBaseUrl;
        public string ChatA3RTApiKey;
        public WeatherSkill.WeatherLocation WeatherLocation = WeatherSkill.WeatherLocation.Tokyo;

        protected override void Awake()
        {
            var translationSkill = gameObject.GetComponent<TranslateSkill>();
            translationSkill.ApiKey = TranslationApiKey;
            translationSkill.Engine = TranslateSkill.TranslationEngine.Watson;
            translationSkill.BaseUrl = TranslationBaseUrl;
            gameObject.GetComponent<ChatA3RTSkill>().A3RTApiKey = ChatA3RTApiKey;
            gameObject.GetComponent<WeatherSkill>().MyLocation = WeatherLocation;

            base.Awake();

            gameObject.GetComponent<QRCodeRequestProvider>().ChatdollCamera.DecodeCode = QRCodeDecoder.DecodeByZXing;
        }
    }
}

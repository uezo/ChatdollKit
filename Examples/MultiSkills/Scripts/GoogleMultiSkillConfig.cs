using UnityEngine;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Examples.MultiSkills
{
    public class GoogleMultiSkillConfig : GoogleApplicationConfig
    {
        [Header("Google Translation API")]
        public string TranslationApiKey;
        public string TranslationBaseUrl;

        [Header("ChatA3RT")]
        public string ChatA3RTApiKey;
    }
}

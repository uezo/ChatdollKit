using UnityEngine;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Examples.MultiSkills
{
    public class WatsonMultiSkillConfig : WatsonApplicationConfig
    {
        [Header("Watson Translation API")]
        public string TranslationApiKey;
        public string TranslationBaseUrl;

        [Header("ChatA3RT")]
        public string ChatA3RTApiKey;
    }
}

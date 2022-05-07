using UnityEngine;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Examples.MultiSkills
{
    public class WatsonMultiSkillConfig : ChatdollKitWatsonConfig
    {
        [Header("Watson Translation API")]
        public string TranslationApiKey;
        public string TranslationBaseUrl;
    }
}

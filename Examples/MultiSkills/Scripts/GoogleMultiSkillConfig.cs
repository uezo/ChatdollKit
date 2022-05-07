using UnityEngine;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Examples.MultiSkills
{
    public class GoogleMultiSkillConfig : ChatdollKitGoogleConfig
    {
        [Header("Google Translation API")]
        public string TranslationApiKey;
        public string TranslationBaseUrl;
    }
}

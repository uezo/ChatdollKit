using UnityEngine;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.MultiSkills
{
    public class AzureMultiSkillConfig : ChatdollKitAzureConfig
    {
        [Header("Azure Translation API")]
        public string TranslationApiKey;
        public string TranslationBaseUrl;
    }
}

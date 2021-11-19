using UnityEngine;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.MultiSkills
{
    public class AzureMultiSkillConfig : AzureApplicationConfig
    {
        [Header("Azure Translation API")]
        public string TranslationApiKey;
        public string TranslationBaseUrl;

        [Header("ChatA3RT")]
        public string ChatA3RTApiKey;
    }
}

using UnityEngine;

namespace ChatdollKit.Extension.Azure
{
    public class AzureApplicationConfig : ScriptableObject
    {
        [Header("Remote Log")]
        public string LogTableUri;

        [Header("Azure Speech Services")]
        public string SpeechApiKey;
        public string Region;
        public string Language;
        public string Gender;
        public string SpeakerName;
    }
}

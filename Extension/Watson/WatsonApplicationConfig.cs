using UnityEngine;

namespace ChatdollKit.Extension.Watson
{
    public class WatsonApplicationConfig : ScriptableObject
    {
        [Header("Watson Speach-to-Text Service")]
        public string STTApiKey;
        public string STTBaseUrl;
        public string STTModel;
        public bool STTRemoveWordSeparation;

        [Header("Watson Text-to-Speach Service")]
        public string TTSApiKey;
        public string TTSBaseUrl;
        public string TTSSpeakerName;
    }
}

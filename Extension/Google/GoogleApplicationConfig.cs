using UnityEngine;

namespace ChatdollKit.Extension.Google
{
    public class GoogleApplicationConfig : ScriptableObject
    {
        [Header("Google Speech API")]
        public string SpeechApiKey;
        public string Language;
        public string Gender;
        public string SpeakerName;
    }
}

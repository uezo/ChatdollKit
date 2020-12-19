using UnityEngine;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Extension.Gatebox
{
    [RequireComponent(typeof(GoogleWakeWordListener))]
    [RequireComponent(typeof(GoogleVoiceRequestProvider))]
    [RequireComponent(typeof(GoogleTTSLoader))]
    public class GateboxApplicationGoogle : GateboxApplication
    {
        [Header("Google Cloud Speech API")]
        public string ApiKey;
        public string Language;

        protected override void Awake()
        {
            GoogleApplication.Configure(gameObject, ApiKey, Language);
            base.Awake();
        }
    }
}

using UnityEngine;

namespace ChatdollKit.Extension
{
    [RequireComponent(typeof(GoogleWakeWordListener))]
    [RequireComponent(typeof(GoogleVoiceRequestProvider))]
    [RequireComponent(typeof(GoogleTTSLoader))]
    public class GoogleApplication : ChatdollApplication
    {
        [Header("Google Cloud Speech API")]
        public string ApiKey;
        public string Language;

        protected override void Awake()
        {
            Configure(gameObject, ApiKey, Language);
            base.Awake();
        }

        public static void Configure(GameObject gameObject, string apiKey, string language)
        {
            // Set API key and language to each component
            var wakewordListener = gameObject.GetComponent<GoogleWakeWordListener>();
            if (wakewordListener != null)
            {
                wakewordListener.ApiKey = string.IsNullOrEmpty(wakewordListener.ApiKey) ? apiKey : wakewordListener.ApiKey;
                wakewordListener.Language = string.IsNullOrEmpty(wakewordListener.Language) ? language : wakewordListener.Language;
            }

            var voiceRequestProvider = gameObject.GetComponent<GoogleVoiceRequestProvider>();
            if (voiceRequestProvider != null)
            {
                voiceRequestProvider.ApiKey = string.IsNullOrEmpty(voiceRequestProvider.ApiKey) ? apiKey : voiceRequestProvider.ApiKey;
                voiceRequestProvider.Language = string.IsNullOrEmpty(voiceRequestProvider.Language) ? language : voiceRequestProvider.Language;
            }

            var ttsLoader = gameObject.GetComponent<GoogleTTSLoader>();
            if (ttsLoader != null)
            {
                ttsLoader.ApiKey = string.IsNullOrEmpty(ttsLoader.ApiKey) ? apiKey : ttsLoader.ApiKey;
                ttsLoader.Language = string.IsNullOrEmpty(ttsLoader.Language) ? language : ttsLoader.Language;
            }
        }
    }
}

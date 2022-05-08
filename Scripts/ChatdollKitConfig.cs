using UnityEngine;

namespace ChatdollKit
{
    public class ChatdollKitConfig : ScriptableObject
    {
        public string ApplicationName;
        public CloudService SpeechService = CloudService.Other;

        [Header("Azure Speech Service Configuration")]
        public string AzureApiKey = string.Empty;
        public string AzureRegion = string.Empty;
        public string AzureLanguage = "ja-JP";
        public string AzureGender = "Female";
        public string AzureSpeakerName = "ja-JP-HarukaRUS";

        [Header("Google Speech Service Configuration")]
        public string GoogleApiKey = string.Empty;
        public string GoogleLanguage = "ja-JP";
        public string GoogleGender = "FEMALE";
        public string GoogleSpeakerName = "ja-JP-Standard-A";

        [Header("Watson Speech Service Configuration")]
        public string WatsonTTSApiKey = string.Empty;
        public string WatsonTTSBaseUrl = string.Empty;
        public string WatsonTTSSpeakerName = "ja-JP_EmiV3Voice";
        public string WatsonSTTApiKey = string.Empty;
        public string WatsonSTTBaseUrl = string.Empty;
        public string WatsonSTTModel = "ja-JP_BroadbandModel";
        public bool WatsonSTTRemoveWordSeparation = true;

        public static ChatdollKitConfig Create(ChatdollKit chatdollKit, ChatdollKitConfig extendedConfig = null)
        {
            var config = extendedConfig == null ? ChatdollKitConfig.CreateInstance<ChatdollKitConfig>() : extendedConfig;

            config.ApplicationName = chatdollKit.ApplicationName;
            config.SpeechService = chatdollKit.SpeechService;

            config.AzureApiKey = chatdollKit.AzureApiKey;
            config.AzureRegion = chatdollKit.AzureRegion;
            config.AzureLanguage = chatdollKit.AzureLanguage;
            config.AzureGender = chatdollKit.AzureGender;
            config.AzureSpeakerName = chatdollKit.AzureSpeakerName;

            config.GoogleApiKey = chatdollKit.GoogleApiKey;
            config.GoogleLanguage = chatdollKit.GoogleLanguage;
            config.GoogleGender = chatdollKit.GoogleGender;
            config.GoogleSpeakerName = chatdollKit.GoogleSpeakerName;

            config.WatsonTTSApiKey = chatdollKit.WatsonTTSApiKey;
            config.WatsonTTSBaseUrl = chatdollKit.WatsonTTSBaseUrl;
            config.WatsonTTSSpeakerName = chatdollKit.WatsonTTSSpeakerName;
            config.WatsonSTTApiKey = chatdollKit.WatsonSTTApiKey;
            config.WatsonSTTBaseUrl = chatdollKit.WatsonSTTBaseUrl;
            config.WatsonSTTModel = chatdollKit.WatsonSTTModel;
            config.WatsonSTTRemoveWordSeparation = chatdollKit.WatsonSTTRemoveWordSeparation;

            return config;
        }

        public static ChatdollKitConfig Load(ChatdollKit chatdollKit, string applicationName, ChatdollKitConfig extendedConfig = null)
        {
            var config = extendedConfig == null ? Resources.Load<ChatdollKitConfig>(applicationName) : extendedConfig;

            if (config == null)
            {
                return null;
            }

            chatdollKit.ApplicationName = config.ApplicationName;
            chatdollKit.SpeechService = config.SpeechService;

            chatdollKit.AzureApiKey = config.AzureApiKey;
            chatdollKit.AzureRegion = config.AzureRegion;
            chatdollKit.AzureLanguage = config.AzureLanguage;
            chatdollKit.AzureGender = config.AzureGender;
            chatdollKit.AzureSpeakerName = config.AzureSpeakerName;

            chatdollKit.GoogleApiKey = config.GoogleApiKey;
            chatdollKit.GoogleLanguage = config.GoogleLanguage;
            chatdollKit.GoogleGender = config.GoogleGender;
            chatdollKit.GoogleSpeakerName = config.GoogleSpeakerName;

            chatdollKit.WatsonTTSApiKey = config.WatsonTTSApiKey;
            chatdollKit.WatsonTTSBaseUrl = config.WatsonTTSBaseUrl;
            chatdollKit.WatsonTTSSpeakerName = config.WatsonTTSSpeakerName;
            chatdollKit.WatsonSTTApiKey = config.WatsonSTTApiKey;
            chatdollKit.WatsonSTTBaseUrl = config.WatsonSTTBaseUrl;
            chatdollKit.WatsonSTTModel = config.WatsonSTTModel;
            chatdollKit.WatsonSTTRemoveWordSeparation = config.WatsonSTTRemoveWordSeparation;

            return config;
        }
    }
}

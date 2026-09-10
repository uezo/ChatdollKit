using System;

namespace ChatdollKit.UI.MessageWindow
{
    /// <summary>Local display preferences. Conversation policy belongs to the message source.</summary>
    [Serializable]
    public sealed class MessageWindowOptions
    {
        public bool ShowUserMessages = true;
        public bool ShowAssistantMessages = true;
        public string UserSpeakerName = "User";
        public string AssistantSpeakerName = "AI";
        public bool AnimateUserText;
        public bool AutoHideUser = true;
        public bool AutoHideAssistant = true;
        public float UserHoldSeconds = 0.7f;
        public float AssistantHoldSeconds = 0.7f;
        public bool ShowUserSpeechPrompt = true;
        public string UserSpeechPrompt = "…";
        /// <summary>Voiced seconds required only for the placeholder. Recognition text is always immediate.</summary>
        public float UserSpeechPromptDelay = 1f;
        public float UserSpeechPromptSilenceTolerance = 0.2f;
        /// <summary>Hide an unresolved placeholder after this many seconds without voice. Zero disables expiry.</summary>
        public float UserSpeechPromptTimeout = 5f;
        public bool ShowListeningPrompt;
        public string ListeningPrompt = "Listening...";
    }
}

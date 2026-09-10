using System;

namespace ChatdollKit.Orchestration
{
    public enum AssistantMessageTiming { PresentationStarted, ResponseReceived }
    public enum AssistantMessageMode { Append, Replace }

    /// <summary>Conversation-side decisions about which messages are eligible for display.</summary>
    [Serializable]
    public sealed class ConversationDisplayOptions
    {
        public string InternalRequestPrefix = "$";
        public bool ShowWakewordMessages = true;
        public AssistantMessageTiming AssistantTiming = AssistantMessageTiming.PresentationStarted;
        public AssistantMessageMode AssistantMode = AssistantMessageMode.Append;

        public ConversationDisplayOptions Copy() => (ConversationDisplayOptions)MemberwiseClone();
    }
}

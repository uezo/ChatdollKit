using System;

namespace ChatdollKit.Orchestration
{
    /// <summary>Conversation facts for logs, subtitles and other observers. Subscribe and read on
    /// Unity's main thread. CurrentConversation is a catch-up snapshot, not a replay of event history.</summary>
    public interface IConversationEventSource
    {
        ConversationSnapshot CurrentConversation { get; }
        event Action<ConversationEvent> ConversationEventReceived;
    }
}

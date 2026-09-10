namespace ChatdollKit.Orchestration
{
    /// <summary>Current conversation and presentation state for a newly attached observer.
    /// This is not a log: subscribe to ConversationEventReceived to retain every event.</summary>
    public sealed class ConversationSnapshot
    {
        public long Sequence { get; set; }
        public string RunId { get; set; }
        public long Generation { get; set; }
        public string SessionId { get; set; }
        public string ContextId { get; set; }
        public bool IsRunning { get; set; }
        public bool CanListen { get; set; }
        public ConversationUtterance LatestUserUtterance { get; set; }
        public ConversationUtterance LatestAssistantUtterance { get; set; }
        // These are separate from latest content: a hidden internal request does not displace
        // the previous eligible utterance when a subtitle/window attaches later.
        public ConversationUtterance DisplayUserUtterance { get; set; }
        public ConversationUtterance DisplayAssistantUtterance { get; set; }

        public ConversationSnapshot Copy() => new ConversationSnapshot
        {
            Sequence = Sequence, RunId = RunId, Generation = Generation, SessionId = SessionId,
            ContextId = ContextId, IsRunning = IsRunning, CanListen = CanListen,
            LatestUserUtterance = LatestUserUtterance?.Copy(), LatestAssistantUtterance = LatestAssistantUtterance?.Copy(),
            DisplayUserUtterance = DisplayUserUtterance?.Copy(), DisplayAssistantUtterance = DisplayAssistantUtterance?.Copy()
        };
    }
}

using Newtonsoft.Json.Linq;

namespace ChatdollKit.Orchestration
{
    /// <summary>Original input to the optional application display filter. Filtering never changes a request.</summary>
    public sealed class ConversationMessageContext
    {
        public ConversationSpeaker Speaker { get; internal set; }
        public string Text { get; internal set; }
        public bool IsAwaitingRecognition { get; internal set; }
        public string SessionId { get; internal set; }
        public string ContextId { get; internal set; }
        public string TransactionId { get; internal set; }
        public JObject Metadata { get; internal set; }
    }
}

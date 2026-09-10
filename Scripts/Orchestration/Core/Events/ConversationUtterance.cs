using Newtonsoft.Json.Linq;

namespace ChatdollKit.Orchestration
{
    public enum ConversationSpeaker { User, Assistant }

    /// <summary>An utterance with original content and the source's presentation decision.
    /// Hidden utterances remain available to event observers and the latest-utterance snapshot.</summary>
    public sealed class ConversationUtterance
    {
        public string Id { get; set; }
        public ConversationSpeaker Speaker { get; set; }
        public string SessionId { get; set; }
        public string ContextId { get; set; }
        public string TransactionId { get; set; }
        public string RecognitionId { get; set; }
        public long Order { get; set; }
        public string Text { get; set; }
        public string VoiceText { get; set; }
        public JObject Metadata { get; set; }
        /// <summary>Speech has started but recognition text has not arrived. The UI chooses any placeholder.</summary>
        public bool IsAwaitingRecognition { get; set; }
        public bool IsPartial { get; set; }
        public bool IsComplete { get; set; }
        public bool IsCanceled { get; set; }
        public bool IsDisplayAllowed { get; set; }
        /// <summary>Source-selected text for presentation. May omit internal requests or filtered segments;
        /// Text and VoiceText retain the original content for other consumers.</summary>
        public string DisplayText { get; set; }

        public ConversationUtterance Copy()
        {
            var copy = (ConversationUtterance)MemberwiseClone();
            copy.Metadata = (JObject)Metadata?.DeepClone();
            return copy;
        }
    }
}

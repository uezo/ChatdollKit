using System;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.Orchestration
{
    public enum ConversationEventKind
    {
        Started, Stopped, Interrupted, Reset, ListeningChanged, StateChanged,
        TurnAccepted, TurnStarted, UserSpeechPartial, UserSpeechConfirmed, UserSpeechCanceled,
        AssistantGenerated, AssistantGenerationCompleted, AssistantSpeechStarted, AssistantSpeechCompleted,
        TurnEnded, ToolCalled, ResponseReceived, Error, UserSpeechStarted, UserSpeechActivity
    }
    public enum ConversationTurnEndReason { Completed, Canceled, Failed }

    /// <summary>One conversation fact, independent of a particular display. Text/VoiceText are the
    /// original segment; Utterance contains cumulative content and its display eligibility.
    /// Payloads are copied per observer. Sequence is monotonic over the source's lifetime.</summary>
    public sealed class ConversationEvent
    {
        public long Sequence { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public ConversationEventKind Kind { get; set; }
        public string RunId { get; set; }
        public long Generation { get; set; }
        public string SessionId { get; set; }
        public string ContextId { get; set; }
        public string TransactionId { get; set; }
        public string RecognitionId { get; set; }
        public ConversationUtterance Utterance { get; set; }
        public string Text { get; set; }
        public string VoiceText { get; set; }
        public JObject Metadata { get; set; }
        public JObject ToolCall { get; set; }
        public JObject StructuredContent { get; set; }
        public string ResponseType { get; set; }
        public ConversationTurnEndReason? EndReason { get; set; }
        public bool? CanListen { get; set; }
        /// <summary>An observed activity fact, independent of recognition text and display timing.</summary>
        public bool? IsSpeechActive { get; set; }
        /// <summary>Classified audio duration, or null when the source supplies an instantaneous observation.</summary>
        public double? AudioDurationSeconds { get; set; }
        /// <summary>Source monotonic observation time. Compare within one source run, not against Unity's clock.</summary>
        public double ObservedAtSeconds { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorType { get; set; }

        public ConversationEvent Copy()
        {
            var copy = (ConversationEvent)MemberwiseClone();
            copy.Utterance = Utterance?.Copy();
            copy.Metadata = (JObject)Metadata?.DeepClone();
            copy.ToolCall = (JObject)ToolCall?.DeepClone();
            copy.StructuredContent = (JObject)StructuredContent?.DeepClone();
            return copy;
        }
    }
}

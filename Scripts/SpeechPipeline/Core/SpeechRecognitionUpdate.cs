namespace ChatdollKit.SpeechPipeline
{
    public enum SpeechRecognitionUpdateKind { Partial, Confirmed, Canceled, Started, Activity }

    /// <summary>One utterance's recognition state and activity observations. Activity updates
    /// do not replace its last transcript. TransactionId is available once a request is accepted.</summary>
    public sealed class SpeechRecognitionUpdate
    {
        public string RecognitionId { get; set; }
        public string SessionId { get; set; }
        public string TransactionId { get; set; }
        public string Text { get; set; }
        public SpeechRecognitionUpdateKind Kind { get; set; }
        /// <summary>Activity observed by Started or Activity. Null when the update carries no activity observation.</summary>
        public bool? IsSpeechActive { get; set; }
        /// <summary>Duration of the classified audio chunk, or null for an instantaneous observation without audio timing.</summary>
        public double? AudioDurationSeconds { get; set; }
        /// <summary>Monotonic observation time at the source. Compare only within the same source run;
        /// it is not the time the notification was delivered to Unity.</summary>
        public double ObservedAtSeconds { get; set; }
        public SpeechRecognitionUpdate Copy() => (SpeechRecognitionUpdate)MemberwiseClone();
    }
}

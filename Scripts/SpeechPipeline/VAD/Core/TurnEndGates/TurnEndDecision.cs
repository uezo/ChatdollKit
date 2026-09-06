namespace ChatdollKit.SpeechPipeline.VAD.TurnEndGates
{
    public sealed class TurnEndDecision
    {
        public bool ShouldEnd { get; set; }
        public double? Confidence { get; set; }
        public string Reason { get; set; }
        /// <summary>Additional audio silence allowed after the VAD silence threshold.</summary>
        public double? Timeout { get; set; }
        public bool Pending { get; set; }
    }
}

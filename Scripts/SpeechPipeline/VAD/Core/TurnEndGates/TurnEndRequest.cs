using ChatdollKit.SpeechPipeline.VAD;
namespace ChatdollKit.SpeechPipeline.VAD.TurnEndGates
{
    public sealed class TurnEndRequest
    {
        public byte[] Audio { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public double RecordedDuration { get; set; }
        public double SilenceDuration { get; set; }
        public string SessionId { get; set; }
        public string Text { get; set; }
        public RecordingSession Session { get; set; }
        public TurnEndGateContext Context { get; set; }

        internal TurnEndRequest WithContext(TurnEndGateContext context)
        {
            return new TurnEndRequest
            {
                Audio = Audio, SampleRate = SampleRate, Channels = Channels,
                RecordedDuration = RecordedDuration, SilenceDuration = SilenceDuration,
                SessionId = SessionId, Text = Text, Session = Session, Context = context
            };
        }
    }
}

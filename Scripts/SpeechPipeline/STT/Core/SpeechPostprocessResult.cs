using System.Collections.Generic;

namespace ChatdollKit.SpeechPipeline.STT
{
    public sealed class SpeechPostprocessResult
    {
        public string Text { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }
}

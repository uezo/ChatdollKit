using System.Collections.Generic;

namespace ChatdollKit.SpeechPipeline.STT
{
    public sealed class SpeechRecognitionResult
    {
        public string Text { get; set; }
        public Dictionary<string, object> PreprocessMetadata { get; set; }
        public Dictionary<string, object> PostprocessMetadata { get; set; }
    }
}

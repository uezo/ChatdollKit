using System.Collections.Generic;

namespace ChatdollKit.SpeechPipeline.STT
{
    public sealed class SpeechPreprocessResult
    {
        /// <summary>Null or empty audio skips transcription and postprocessing.</summary>
        public byte[] Audio { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }
}

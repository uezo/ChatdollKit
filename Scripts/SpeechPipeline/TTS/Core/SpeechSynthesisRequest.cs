using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public sealed class SpeechSynthesisRequest
    {
        public string Text { get; set; }
        public JObject StyleInfo { get; set; }
        public string Language { get; set; }
        public SpeechSynthesisRequest Copy() => new SpeechSynthesisRequest
        { Text = Text, StyleInfo = (JObject)StyleInfo?.DeepClone(), Language = Language };
    }
}

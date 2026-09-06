using ChatdollKit.SpeechPipeline;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public abstract class SpeechSynthesizerComponent : LiveSpeechComponent
    {
        public ISpeechSynthesizer Synthesizer { get; internal set; }
        public abstract SpeechSynthesizerOptions BuildOptions(SpeechSynthesizerOptions current = null);
        public abstract ISpeechSynthesizer CreateSynthesizer(SpeechSynthesizerOptions options);
    }
}

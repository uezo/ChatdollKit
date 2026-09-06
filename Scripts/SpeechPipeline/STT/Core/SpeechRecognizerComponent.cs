using ChatdollKit.SpeechPipeline;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.STT
{
    public abstract class SpeechRecognizerComponent : LiveSpeechComponent
    {
        public ISpeechRecognizer Recognizer { get; internal set; }
        public abstract SpeechRecognizerOptions BuildOptions(SpeechRecognizerOptions current = null);
        public abstract ISpeechRecognizer CreateRecognizer(SpeechRecognizerOptions options);
        public virtual SpeechRecognizerOptions ReadOptions(ISpeechRecognizer recognizer)
            => ((SpeechRecognizerBase)recognizer).GetOptions();
        public virtual UniTask ApplyRecognizerOptionsAsync(ISpeechRecognizer recognizer, SpeechRecognizerOptions options, CancellationToken token)
        { token.ThrowIfCancellationRequested(); ((SpeechRecognizerBase)recognizer).UpdateOptions(options); return UniTask.CompletedTask; }
    }
}

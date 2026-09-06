using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.VAD.Silero;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.STT;

namespace ChatdollKit.SpeechPipeline.VAD
{
    public abstract class SpeechDetectorComponent : LiveSpeechComponent
    {
        public ISpeechDetector Detector { get; internal set; }
        public abstract SpeechDetectorOptions BuildOptions();
        public abstract UniTask<SpeechDetectorLease> CreateDetectorAsync(ISpeechRecognizer recognizer, CancellationToken cancellationToken);
        public virtual UniTask ApplyDetectorOptionsAsync(ISpeechDetector detector, SpeechDetectorOptions options, CancellationToken cancellationToken)
            => ((SpeechDetectorBase)detector).UpdateRuntimeOptionsAsync(options, cancellationToken);
        public override string GetRestartKey()
        {
            var options = BuildOptions();
            return options.SampleRate + "/" + options.Channels + "/" + options.PrerollBufferCount + "/" +
                ((options as SileroSpeechDetectorOptions)?.UseVadIterator ?? false);
        }
    }
}

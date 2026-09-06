using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public interface ITtsPreprocessor
    {
        UniTask<string> ProcessAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options,
            CancellationToken cancellationToken = default);
    }
}

using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public interface ISpeechSynthesizer
    {
        UniTask<byte[]> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default);
        UniTask<byte[]> GenerateAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default);
        SpeechSynthesizerOptions GetOptions();
        void UpdateOptions(SpeechSynthesizerOptions options);
        UniTask DisposeAsync();
    }
}

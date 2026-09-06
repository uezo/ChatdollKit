using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.STT
{
    /// <summary>Recognizes a complete snapshot of raw, signed 16-bit little-endian PCM.</summary>
    /// <remarks>The recognizer's audio format must match the detector's format.</remarks>
    public interface ISpeechRecognizer
    {
        UniTask<SpeechRecognitionResult> RecognizeAsync(
            string sessionId, byte[] audio, CancellationToken cancellationToken = default);
    }
}

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.VAD.Silero
{
    /// <summary>A stateful Silero model. Ownership stays with the injecting caller.
    /// Implementations serialize inference and prevent canceled work from overwriting reset state.</summary>
    public interface ISileroVadModel : IDisposable
    {
        UniTask<float> PredictAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default);
        void ResetStates();
    }
}

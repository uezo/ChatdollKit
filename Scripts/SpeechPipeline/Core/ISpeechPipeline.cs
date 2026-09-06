using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline
{
    /// <summary>One conversation, independent of presentation and transport encoding.</summary>
    public interface ISpeechPipeline
    {
        string SessionId { get; }
        event Func<SpeechPipelineResponse, UniTask> ResponseReceived;
        event Action<Exception> Error;
        UniTask<SpeechPipelineResponse> InvokeAsync(SpeechPipelineRequest request, CancellationToken cancellationToken = default);
        UniTask ProcessAudioSamplesAsync(byte[] samples, CancellationToken cancellationToken = default);
        UniTask InterruptAsync(CancellationToken cancellationToken = default);
        UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default);
        UniTask DrainAsync();
        UniTask DisposeAsync();
    }
}

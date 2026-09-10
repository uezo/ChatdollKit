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
        /// <summary>Resets current speech input, including captured audio and unfinished recognition.
        /// Retains requests already delivered to the pipeline, responses and conversation history.
        /// May be awaited from ResponseReceived; waits for input reset, not requests or response delivery.
        /// Backends that own this reset may complete immediately.</summary>
        UniTask ResetSpeechInputAsync(CancellationToken cancellationToken = default);
        UniTask InterruptAsync(CancellationToken cancellationToken = default);
        UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default);
        UniTask DrainAsync();
        UniTask DisposeAsync();
    }
}

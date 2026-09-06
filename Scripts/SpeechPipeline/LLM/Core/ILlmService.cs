using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public interface ILlmService
    {
        /// <summary>Streams speech-sized chunks to the callback and returns the final result.
        /// Await the returned task to finish consuming and releasing the underlying stream.</summary>
        UniTask<LlmResult> ChatAsync(LlmRequest request, Func<LlmResponse, UniTask> onResponse = null,
            CancellationToken cancellationToken = default);
        LlmServiceOptions GetOptions();
        void UpdateOptions(LlmServiceOptions options);
        UniTask DisposeAsync();
    }
}

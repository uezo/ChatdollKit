using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>A single text WebSocket connection. A pool leases it to one request at a time.</summary>
    public interface ILlmWebSocketConnection : IDisposable
    {
        bool IsOpen { get; }
        UniTask ConnectAsync(Uri uri, string apiKey, CancellationToken cancellationToken);
        UniTask SendTextAsync(string message, CancellationToken cancellationToken);
        /// <summary>Returns one complete UTF-8 message, or null when the peer closes the connection.</summary>
        UniTask<string> ReceiveTextAsync(CancellationToken cancellationToken);
        void Abort();
    }
}

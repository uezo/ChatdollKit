using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.Remote
{
    /// <summary>One WebSocket connection carrying complete AIAvatarKit JSON messages.</summary>
    public interface IAIAvatarConnection : IDisposable
    {
        bool IsOpen { get; }
        UniTask ConnectAsync(Uri uri, string apiKey, CancellationToken cancellationToken);
        UniTask SendAsync(string message, CancellationToken cancellationToken);
        /// <summary>Returns one complete text message, or null when the peer closes.</summary>
        UniTask<string> ReceiveAsync(CancellationToken cancellationToken);
        void Abort();
    }
}

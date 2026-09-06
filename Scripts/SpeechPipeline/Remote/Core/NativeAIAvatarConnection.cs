using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using ChatdollKit.SpeechPipeline.Async;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.Remote
{
    /// <summary>Native transport. The pipeline serializes writes and owns one receive loop.</summary>
    public sealed class NativeAIAvatarConnection : IAIAvatarConnection
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly ClientWebSocket socket;
        private readonly byte[] buffer = new byte[8192];
        private readonly int maximumMessageBytes;
        private int disposed;

        public NativeAIAvatarConnection(int maximumMessageBytes = 32 * 1024 * 1024)
        {
            if (maximumMessageBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
            this.maximumMessageBytes = maximumMessageBytes;
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("Use WebGLAIAvatarConnection on the Web platform.");
#else
            socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
#endif
        }
        public bool IsOpen => Volatile.Read(ref disposed) == 0 && socket?.State == WebSocketState.Open;

        public async UniTask ConnectAsync(Uri uri, string apiKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(apiKey)) socket.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
            using (cancellationToken.Register(Abort))
                await SpeechAsync.FromTask(socket.ConnectAsync(uri, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }

        public async UniTask SendAsync(string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = Utf8.GetBytes(message ?? throw new ArgumentNullException(nameof(message)));
            using (cancellationToken.Register(Abort))
                await SpeechAsync.FromTask(socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                    true, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }

        public async UniTask<string> ReceiveAsync(CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(Abort))
            using (var message = new MemoryStream())
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var part = await SpeechAsync.FromTask(socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken));
                    cancellationToken.ThrowIfCancellationRequested();
                    if (part.MessageType == WebSocketMessageType.Close) return null;
                    if (part.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Expected an AIAvatarKit text message.");
                    if (message.Length + part.Count > maximumMessageBytes) throw new InvalidDataException("AIAvatarKit message exceeded the size limit.");
                    message.Write(buffer, 0, part.Count);
                    if (part.EndOfMessage) return Utf8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
                }
            }
        }

        public void Abort()
        {
            try { socket?.Abort(); } catch (ObjectDisposedException) { }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { Abort(); } finally { socket?.Dispose(); }
        }
    }
}

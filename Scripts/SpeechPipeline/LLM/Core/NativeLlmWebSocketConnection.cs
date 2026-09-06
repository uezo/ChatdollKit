using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>Native-platform WebSocket transport exposed through UniTask.</summary>
    public sealed class NativeLlmWebSocketConnection : ILlmWebSocketConnection
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly ClientWebSocket socket;
        private readonly byte[] receiveBuffer = new byte[8192];
        private int disposed;

        public NativeLlmWebSocketConnection()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("Responses WebSocket requires a native platform; WebGL needs a browser WebSocket transport.");
#else
            socket = new ClientWebSocket();
#endif
        }

        public bool IsOpen => Volatile.Read(ref disposed) == 0 && socket?.State == WebSocketState.Open;

        public async UniTask ConnectAsync(Uri uri, string apiKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
            using (cancellationToken.Register(Abort))
                await SpeechAsync.FromTask(socket.ConnectAsync(uri, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }

        public async UniTask SendTextAsync(string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = Utf8.GetBytes(message);
            using (cancellationToken.Register(Abort))
                await SpeechAsync.FromTask(socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true,
                    cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }

        public async UniTask<string> ReceiveTextAsync(CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(Abort))
            using (var message = new MemoryStream())
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var received = await SpeechAsync.FromTask(socket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer),
                        cancellationToken));
                    cancellationToken.ThrowIfCancellationRequested();
                    if (received.MessageType == WebSocketMessageType.Close) return null;
                    if (received.MessageType != WebSocketMessageType.Text)
                        throw new InvalidDataException("Expected a text WebSocket message.");
                    message.Write(receiveBuffer, 0, received.Count);
                    // UTF-8 characters may cross receive buffers or WebSocket fragments.
                    if (received.EndOfMessage)
                        return Utf8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
                }
            }
        }

        public void Abort()
        {
            try { socket?.Abort(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { Abort(); }
            finally { socket?.Dispose(); }
        }
    }
}

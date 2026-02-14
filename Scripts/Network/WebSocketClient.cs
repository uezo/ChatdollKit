using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

#if UNITY_WEBGL && !UNITY_EDITOR && NATIVEWEBSOCKET
using NativeWebSocket;
#elif !UNITY_WEBGL || UNITY_EDITOR
using System.Net.WebSockets;
using System.Text;
#endif

namespace ChatdollKit.Network
{
    internal interface IWebSocketClient : IDisposable
    {
        bool IsConnected { get; }
        event Action<string> OnMessage;
        UniTask ConnectAsync(string url, CancellationToken token, Dictionary<string, string> headers = null);
        UniTask SendTextAsync(string message, CancellationToken token);
        UniTask CloseAsync();
    }

#if UNITY_WEBGL && !UNITY_EDITOR && NATIVEWEBSOCKET

    internal class WebSocketClient : IWebSocketClient
    {
        private WebSocket webSocket;
        private bool disposed;

        public bool IsConnected => webSocket?.State == WebSocketState.Open;
        public event Action<string> OnMessage;

        public async UniTask ConnectAsync(string url, CancellationToken token, Dictionary<string, string> headers = null)
        {
            if (headers != null && headers.Count > 0)
            {
                // Browser WebSocket API doesn't support custom headers.
                // Encode headers as subprotocols: {Key}.{Base64URL(Value)}
                // Server must echo back one of these in Sec-WebSocket-Protocol response header.
                var subprotocols = new List<string>();
                foreach (var header in headers)
                {
                    var value = header.Value;
                    if (value.StartsWith("Bearer "))
                    {
                        value = value.Substring(7);
                    }
                    var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
                        .Replace('+', '-').Replace('/', '_').TrimEnd('=');
                    subprotocols.Add($"{header.Key}.{encoded}");
                }
                webSocket = new WebSocket(url, subprotocols);
            }
            else
            {
                webSocket = new WebSocket(url);
            }

            webSocket.OnMessage += (data) =>
            {
                OnMessage?.Invoke(System.Text.Encoding.UTF8.GetString(data));
            };

            var openTcs = new UniTaskCompletionSource();
            webSocket.OnOpen += () => openTcs.TrySetResult();
            webSocket.OnError += (err) => openTcs.TrySetException(new Exception(err));

            await webSocket.Connect();
            await openTcs.Task;
        }

        public async UniTask SendTextAsync(string message, CancellationToken token)
        {
            if (!IsConnected) return;
            await webSocket.SendText(message);
        }

        public async UniTask CloseAsync()
        {
            if (webSocket != null && webSocket.State == WebSocketState.Open)
            {
                await webSocket.Close();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
        }
    }

#elif UNITY_WEBGL && !UNITY_EDITOR

    internal class WebSocketClient : IWebSocketClient
    {
        public bool IsConnected => false;
        public event Action<string> OnMessage;

        public UniTask ConnectAsync(string url, CancellationToken token, Dictionary<string, string> headers = null)
        {
            UnityEngine.Debug.LogWarning(
                "WebSocketClient requires NativeWebSocket for WebGL builds. " +
                "Please add the NativeWebSocket package to your project: https://github.com/endel/NativeWebSocket");
            return UniTask.CompletedTask;
        }

        public UniTask SendTextAsync(string message, CancellationToken token)
        {
            return UniTask.CompletedTask;
        }

        public UniTask CloseAsync()
        {
            return UniTask.CompletedTask;
        }

        public void Dispose() { }
    }

#else

    internal class WebSocketClient : IWebSocketClient
    {
        private ClientWebSocket webSocket;
        private CancellationTokenSource receiveCts;
        private bool disposed;

        public bool IsConnected => webSocket?.State == WebSocketState.Open;
        public event Action<string> OnMessage;

        public async UniTask ConnectAsync(string url, CancellationToken token, Dictionary<string, string> headers = null)
        {
            webSocket = new ClientWebSocket();
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    webSocket.Options.SetRequestHeader(header.Key, header.Value);
                }
            }
            await webSocket.ConnectAsync(new Uri(url), token);

            receiveCts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(receiveCts.Token);
        }

        private async UniTask ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            var messageBuilder = new StringBuilder();

            try
            {
                while (webSocket != null && webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    messageBuilder.Clear();

                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    OnMessage?.Invoke(messageBuilder.ToString());
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error receiving WebSocket message: {ex.Message}");
            }
        }

        public async UniTask SendTextAsync(string message, CancellationToken token)
        {
            if (!IsConnected) return;
            var buffer = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, token);
        }

        public async UniTask CloseAsync()
        {
            receiveCts?.Cancel();
            if (webSocket != null && webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            receiveCts?.Cancel();
            receiveCts?.Dispose();
            webSocket?.Dispose();
        }
    }

#endif
}

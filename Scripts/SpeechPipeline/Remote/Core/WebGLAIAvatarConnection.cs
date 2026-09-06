using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ChatdollKit.SpeechPipeline.Async;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.Remote
{
    /// <summary>Browser WebSocket transport. Create and use on Unity's main thread.</summary>
    public sealed class WebGLAIAvatarConnection : IAIAvatarConnection
    {
        private readonly Queue<string> messages = new Queue<string>();
        private readonly SpeechCompletionSource<bool> opened = new SpeechCompletionSource<bool>();
        private readonly int maximumBufferedBytes;
        private SpeechCompletionSource<string> receiver;
        private AIAvatarWebSocketBridge bridge;
        private Exception failure;
        private int handle;
        private int bufferedBytes;
        private bool connected;
        private bool ended;
        private bool disposed;
        private bool started;

        public WebGLAIAvatarConnection(int maximumBufferedBytes = 32 * 1024 * 1024)
        {
            if (maximumBufferedBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBufferedBytes));
            this.maximumBufferedBytes = maximumBufferedBytes;
        }

        public bool IsOpen => connected && !ended && !disposed;

        public async UniTask ConnectAsync(Uri uri, string apiKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (disposed) throw new ObjectDisposedException(nameof(WebGLAIAvatarConnection));
            if (started) throw new InvalidOperationException("A WebSocket connection can only be started once.");
            if (uri == null || (uri.Scheme != "ws" && uri.Scheme != "wss")) throw new ArgumentException("Expected a WebSocket URL.", nameof(uri));
            started = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            var protocol = CreateAuthorizationProtocol(apiKey);
            var owner = new GameObject("AIAvatarWebSocket-" + Guid.NewGuid().ToString("N"));
            owner.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(owner);
            bridge = owner.AddComponent<AIAvatarWebSocketBridge>();
            bridge.Connection = this;
            handle = AIAvatarSocketConnect(uri.AbsoluteUri, protocol, owner.name);
            try { await SpeechAsync.WaitAsync(opened.Task, cancellationToken); }
            catch { Abort(); throw; }
#else
            await UniTask.CompletedTask;
            throw new PlatformNotSupportedException("WebGLAIAvatarConnection requires a Web player. Use NativeAIAvatarConnection in the Editor.");
#endif
        }

        /// <summary>Matches AIAvatarKit's Authorization.&lt;unpadded standard Base64&gt; convention.</summary>
        public static string CreateAuthorizationProtocol(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) return string.Empty;
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(apiKey)).TrimEnd('=');
            // The current server uses standard Base64. '/' is not a legal HTTP token;
            // silently substituting base64url would change the key decoded by that server.
            if (encoded.IndexOf('/') >= 0)
                throw new ArgumentException("This API key cannot be encoded by the server's browser authentication convention. Use an ASCII alphanumeric key.", nameof(apiKey));
            return "Authorization." + encoded;
        }

        public async UniTask SendAsync(string message, CancellationToken cancellationToken)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            cancellationToken.ThrowIfCancellationRequested();
            CheckOpen();
#if UNITY_WEBGL && !UNITY_EDITOR
            // Bound browser output buffering when microphone production outpaces the network.
            while (AIAvatarSocketBufferedAmount(handle) > 256 * 1024)
            {
                await SpeechAsync.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                CheckOpen();
            }
            if (AIAvatarSocketSend(handle, message) == 0) throw new IOException("The browser WebSocket could not send the message.");
#else
            await UniTask.CompletedTask;
            throw new PlatformNotSupportedException("Browser WebSocket is unavailable in the Editor.");
#endif
        }

        public async UniTask<string> ReceiveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (disposed) throw new ObjectDisposedException(nameof(WebGLAIAvatarConnection));
            if (receiver != null) throw new InvalidOperationException("Only one WebSocket receive may be pending.");
            if (messages.Count > 0)
            {
                var message = messages.Dequeue();
                bufferedBytes -= Encoding.UTF8.GetByteCount(message);
                return message;
            }
            if (failure != null) throw failure;
            if (ended) return null;
            var pending = receiver = new SpeechCompletionSource<string>();
            try { return await SpeechAsync.WaitAsync(pending.Task, cancellationToken); }
            catch (OperationCanceledException) { Abort(); throw; }
            finally { if (ReferenceEquals(receiver, pending)) receiver = null; }
        }

        internal void OnOpen()
        {
            if (ended || disposed) return;
            connected = true;
            opened.TrySetResult(true);
        }
        internal void OnMessage(string message)
        {
            if (ended || disposed) return;
            var size = Encoding.UTF8.GetByteCount(message);
            if (size > maximumBufferedBytes || bufferedBytes > maximumBufferedBytes - size)
            {
                OnFailure("AIAvatarKit browser receive buffer exceeded its size limit.");
                return;
            }
            if (receiver != null && receiver.Task.Status == UniTaskStatus.Pending)
                receiver.TrySetResult(message);
            else { messages.Enqueue(message); bufferedBytes += size; }
        }
        internal void OnFailure(string reason)
        {
            if (ended || disposed) return;
            Finish(new IOException(reason));
            CloseBrowserSocket();
        }
        internal void OnClose()
        {
            if (ended || disposed) return;
            Finish(null);
        }
        private void Finish(Exception error)
        {
            ended = true;
            connected = false;
            failure = error;
            opened.TrySetException(error ?? new IOException("The browser WebSocket closed before initialization completed."));
            if (error != null) receiver?.TrySetException(error);
            else receiver?.TrySetResult(null);
        }
        private void CheckOpen()
        {
            if (disposed) throw new ObjectDisposedException(nameof(WebGLAIAvatarConnection));
            if (!IsOpen) throw failure ?? new IOException("The browser WebSocket is not open.");
        }
        public void Abort()
        {
            if (disposed) return;
            if (!ended) Finish(new OperationCanceledException("The browser WebSocket was aborted."));
            messages.Clear(); bufferedBytes = 0;
            CloseBrowserSocket();
        }
        public void Dispose()
        {
            if (disposed) return;
            Abort();
            disposed = true;
            if (bridge != null)
            {
                bridge.Connection = null;
                if (Application.isPlaying) UnityEngine.Object.Destroy(bridge.gameObject);
                else UnityEngine.Object.DestroyImmediate(bridge.gameObject);
                bridge = null;
            }
        }
        private void CloseBrowserSocket()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (handle != 0) { AIAvatarSocketDispose(handle); handle = 0; }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int AIAvatarSocketConnect(string url, string protocol, string gameObject);
        [DllImport("__Internal")] private static extern int AIAvatarSocketSend(int handle, string message);
        [DllImport("__Internal")] private static extern int AIAvatarSocketBufferedAmount(int handle);
        [DllImport("__Internal")] private static extern void AIAvatarSocketDispose(int handle);
#endif
    }
}

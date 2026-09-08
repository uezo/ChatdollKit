using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ChatdollKit.SpeechPipeline.Async;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.VAD.Silero.WebGL
{
    /// <summary>Browser-only Silero inference. Create and use on Unity's main thread.</summary>
    public sealed class WebGLSileroVadModel : ISileroVadModel
    {
        public const string DefaultRuntimeScriptUrl = "https://cdn.jsdelivr.net/npm/onnxruntime-web@1.21.0/dist/ort.wasm.min.js";
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly SpeechCompletionSource<bool> initialized = new SpeechCompletionSource<bool>();
        private SileroVadWebGLBridge bridge;
        private Prediction pending;
        private Exception failure;
        private int handle;
        private int nextRequest;
        private bool ready;
        private bool disposed;

        private sealed class Prediction
        {
            internal int Id;
            internal readonly SpeechCompletionSource<float> Completion = new SpeechCompletionSource<float>();
        }

        [Serializable]
        private sealed class Reply
        {
            public int requestId;
            public float probability;
            public string message;
        }

        private WebGLSileroVadModel() { }

        public static async UniTask<WebGLSileroVadModel> CreateAsync(string modelUrl, string runtimeScriptUrl,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(modelUrl)) throw new ArgumentException("A model URL is required.", nameof(modelUrl));
            if (string.IsNullOrWhiteSpace(runtimeScriptUrl)) throw new ArgumentException("An ONNX Runtime Web script URL is required.", nameof(runtimeScriptUrl));
#if UNITY_WEBGL && !UNITY_EDITOR
            var model = new WebGLSileroVadModel();
            try
            {
                var owner = new GameObject("SileroVad-" + Guid.NewGuid().ToString("N"));
                owner.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(owner);
                model.bridge = owner.AddComponent<SileroVadWebGLBridge>();
                model.bridge.Model = model;
                model.handle = SileroVadCreate(modelUrl, runtimeScriptUrl, owner.name);
                if (model.handle <= 0) throw new IOException("Could not start browser Silero initialization.");
                using (var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (SpeechAsync.Timeout(source, TimeSpan.FromSeconds(30)))
                {
                    try { await SpeechAsync.WaitAsync(model.initialized.Task, source.Token); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("Silero initialization timed out. Check the model, ONNX Runtime Web files and browser network console.");
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                return model;
            }
            catch { model.Dispose(); throw; }
#else
            await UniTask.CompletedTask;
            throw new PlatformNotSupportedException("WebGLSileroVadModel requires a Web player. Use OnnxSileroVadModel in the Editor.");
#endif
        }

        public async UniTask<float> PredictAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            CheckThread();
            cancellationToken.ThrowIfCancellationRequested();
            CheckAvailable();
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (sampleRate != 16000 && sampleRate != 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate), "Silero supports 8000 or 16000 Hz.");
            var sampleCount = sampleRate == 16000 ? 512 : 256;
            if (samples.Length != sampleCount) throw new ArgumentException($"Expected {sampleCount} samples at {sampleRate} Hz.", nameof(samples));
            if (pending != null) throw new InvalidOperationException("Only one Silero prediction may be awaited at a time.");
            if (nextRequest == int.MaxValue) throw new InvalidOperationException("Restart the Silero detector before issuing more predictions.");
            var request = pending = new Prediction { Id = ++nextRequest };
            try
            {
                SubmitPrediction(request.Id, samples, sampleRate);
                using (var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (SpeechAsync.Timeout(source, TimeSpan.FromSeconds(10)))
                {
                    try
                    {
                        var probability = await SpeechAsync.WaitAsync(request.Completion.Task, source.Token);
                        cancellationToken.ThrowIfCancellationRequested();
                        return probability;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !disposed && ReferenceEquals(pending, request))
                    {
                        throw new TimeoutException("Browser Silero inference timed out.");
                    }
                }
            }
            catch
            {
                // Canceling the managed wait does not stop an ONNX run. Reset invalidates its
                // generation in JS before another request can consume or update that state.
                if (!disposed && failure == null && ReferenceEquals(pending, request)) ResetStates();
                throw;
            }
            finally { if (ReferenceEquals(pending, request)) pending = null; }
        }

        private void SubmitPrediction(int requestId, float[] samples, int sampleRate)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // JavaScript copies the array before returning; no pinned memory survives an await.
            if (SileroVadPredict(handle, requestId, samples, samples.Length, sampleRate) == 0)
                throw new IOException("The browser Silero model could not accept the audio window.");
#else
            throw new PlatformNotSupportedException("Browser Silero inference requires a Web player.");
#endif
        }

        public void ResetStates()
        {
            CheckThread();
            CheckAvailable();
            var request = pending;
            pending = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            SileroVadReset(handle);
#endif
            request?.Completion.TrySetCanceled();
        }

        internal void OnReady()
        {
            if (disposed || failure != null) return;
            ready = true;
            initialized.TrySetResult(true);
        }

        internal void OnResult(string json)
        {
            if (disposed) return;
            try
            {
                var reply = JsonUtility.FromJson<Reply>(json);
                if (reply == null) throw new FormatException();
                if (pending == null || pending.Id != reply.requestId) return;
                if (float.IsNaN(reply.probability) || float.IsInfinity(reply.probability) || reply.probability < 0 || reply.probability > 1)
                    throw new FormatException();
                pending.Completion.TrySetResult(reply.probability);
            }
            catch (Exception error) { Fail(new IOException("Invalid browser Silero response.", error)); }
        }

        internal void OnError(string json)
        {
            if (disposed) return;
            try
            {
                var reply = JsonUtility.FromJson<Reply>(json);
                if (reply == null) throw new FormatException();
                var error = new IOException("Browser Silero: " + (reply.message ?? "Inference failed."));
                if (reply.requestId == 0) Fail(error);
                else if (pending != null && pending.Id == reply.requestId) pending.Completion.TrySetException(error);
            }
            catch (Exception error) { Fail(new IOException("Invalid browser Silero error response.", error)); }
        }

        private void Fail(Exception error)
        {
            failure = error;
            initialized.TrySetException(error);
            pending?.Completion.TrySetException(error);
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Use browser Silero on Unity's main thread.");
        }

        private void CheckAvailable()
        {
            if (disposed) throw new ObjectDisposedException(nameof(WebGLSileroVadModel));
            if (failure != null) throw failure;
            if (!ready) throw new InvalidOperationException("Browser Silero has not initialized.");
        }

        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            disposed = true;
            initialized.TrySetCanceled();
            pending?.Completion.TrySetCanceled();
            pending = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (handle != 0) SileroVadDispose(handle);
#endif
            handle = 0;
            if (bridge != null)
            {
                bridge.Model = null;
                if (Application.isPlaying) UnityEngine.Object.Destroy(bridge.gameObject);
                else UnityEngine.Object.DestroyImmediate(bridge.gameObject);
                bridge = null;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int SileroVadCreate(string modelUrl, string runtimeScriptUrl, string target);
        [DllImport("__Internal")] private static extern int SileroVadPredict(int handle, int requestId, float[] samples, int count, int sampleRate);
        [DllImport("__Internal")] private static extern void SileroVadReset(int handle);
        [DllImport("__Internal")] private static extern void SileroVadDispose(int handle);
#endif
    }
}

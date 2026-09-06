using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;

namespace ChatdollKit.SpeechPipeline.STT
{
    /// <summary>
    /// Shared preprocessing, transcription, and postprocessing for batch STT.
    /// Calls can run concurrently; each captures its audio, options, and hooks.
    /// </summary>
    public abstract class SpeechRecognizerBase : ISpeechRecognizer
    {
        private readonly object sync = new object();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<UniTask> active = new HashSet<UniTask>();
        private readonly SpeechCallbackGuard callbacks = new SpeechCallbackGuard();
        private SpeechRecognizerOptions options;
        private Func<string, byte[], CancellationToken, UniTask<SpeechPreprocessResult>> preprocess;
        private Func<string, string, byte[], Dictionary<string, object>, CancellationToken, UniTask<SpeechPostprocessResult>> postprocess;
        private UniTask disposeTask;
        private bool disposing;

        public Func<string, byte[], CancellationToken, UniTask<SpeechPreprocessResult>> PreprocessAsync
        {
            get { lock (sync) return preprocess; }
            set { lock (sync) { ThrowIfDisposing(); preprocess = value; } }
        }

        public Func<string, string, byte[], Dictionary<string, object>, CancellationToken, UniTask<SpeechPostprocessResult>> PostprocessAsync
        {
            get { lock (sync) return postprocess; }
            set { lock (sync) { ThrowIfDisposing(); postprocess = value; } }
        }

        protected SpeechRecognizerBase(SpeechRecognizerOptions options = null)
        {
            this.options = CopyAndValidate(options ?? new SpeechRecognizerOptions());
        }

        public SpeechRecognizerOptions GetOptions()
        {
            lock (sync) return options.Copy();
        }

        /// <summary>Replaces the configuration for future calls. Active calls retain their snapshots.</summary>
        public void UpdateOptions(SpeechRecognizerOptions replacement)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            var snapshot = CopyAndValidate(replacement);
            lock (sync)
            {
                ThrowIfDisposing();
                if (snapshot.GetType() != options.GetType())
                    throw new ArgumentException("Options must retain the recognizer's configuration type.", nameof(replacement));
                options = snapshot;
            }
        }

        public UniTask<SpeechRecognitionResult> RecognizeAsync(string sessionId, byte[] audio,
            CancellationToken cancellationToken = default)
        {
            return StartOperation(audio, cancellationToken, (request, token) => RecognizeCoreAsync(sessionId, request, token));
        }

        /// <summary>Calls the provider directly, bypassing both hooks.</summary>
        public UniTask<string> TranscribeAsync(byte[] audio, CancellationToken cancellationToken = default)
        {
            return StartOperation(audio, cancellationToken,
                (request, token) => callbacks.InvokeAsync(() => TranscribeCoreAsync(request.Audio, request.Options, token)));
        }

        protected abstract UniTask<string> TranscribeCoreAsync(byte[] audio, SpeechRecognizerOptions options,
            CancellationToken cancellationToken);

        private async UniTask<SpeechRecognitionResult> RecognizeCoreAsync(string sessionId, RequestSnapshot request,
            CancellationToken cancellationToken)
        {
            var result = new SpeechRecognitionResult();
            var audio = request.Audio;
            if (request.Preprocess != null)
            {
                var processed = await callbacks.InvokeAsync(() => request.Preprocess(sessionId, audio, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (processed == null) throw new InvalidOperationException("The speech preprocessing hook returned no result.");
                // Hooks may reuse their output buffers after returning.
                audio = processed.Audio == null ? null : (byte[])processed.Audio.Clone();
                result.PreprocessMetadata = CopyMetadata(processed.Metadata);
            }
            if (audio == null || audio.Length == 0) return result;

            result.Text = await callbacks.InvokeAsync(() => TranscribeCoreAsync(audio, request.Options, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            // Postprocessing still runs when the provider has no transcript.
            if (request.Postprocess != null)
            {
                var processed = await callbacks.InvokeAsync(() => request.Postprocess(sessionId, result.Text, audio,
                    result.PreprocessMetadata, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (processed == null) throw new InvalidOperationException("The speech postprocessing hook returned no result.");
                result.Text = processed.Text;
                result.PostprocessMetadata = CopyMetadata(processed.Metadata);
            }
            return result;
        }

        private UniTask<T> StartOperation<T>(byte[] audio, CancellationToken cancellationToken,
            Func<RequestSnapshot, CancellationToken, UniTask<T>> operation)
        {
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            RequestSnapshot request;
            var completion = new SpeechCompletionSource<T>();
            var activeTask = completion.Task.AsUniTask();
            lock (sync)
            {
                ThrowIfDisposing();
                cancellationToken.ThrowIfCancellationRequested();
                request = new RequestSnapshot
                {
                    Audio = (byte[])audio.Clone(), Options = options.Copy(),
                    Preprocess = preprocess, Postprocess = postprocess
                };
                // Register before invoking caller code, even when every await completes synchronously.
                active.Add(activeTask);
            }
            RunOperationAsync(request, operation, completion, activeTask, cancellationToken).Forget();
            return completion.Task;
        }

        private async UniTask RunOperationAsync<T>(RequestSnapshot request,
            Func<RequestSnapshot, CancellationToken, UniTask<T>> operation, SpeechCompletionSource<T> completion, UniTask activeTask,
            CancellationToken cancellationToken)
        {
            try
            {
                T result;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
                {
                    linked.Token.ThrowIfCancellationRequested();
                    result = await operation(request, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                }
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally
            {
                lock (sync) active.Remove(activeTask);
            }
        }

        /// <summary>
        /// Cancels and awaits owned work before disposing resources. Hooks and
        /// provider methods must not await disposal of their own recognizer.
        /// </summary>
        public UniTask DisposeAsync()
        {
            callbacks.ThrowIfActive("Dispose the recognizer after its hook or transcription call returns.");
            UniTask[] pending;
            SpeechCompletionSource<bool> completion;
            lock (sync)
            {
                if (disposing) return disposeTask;
                disposing = true;
                pending = active.ToArray();
                completion = new SpeechCompletionSource<bool>();
                disposeTask = completion.Task;
            }
            DisposeCoreAsync(pending, completion).Forget();
            return disposeTask;
        }

        private async UniTask DisposeCoreAsync(UniTask[] pending, SpeechCompletionSource<bool> completion)
        {
            Exception failure = null;
            try { lifetime.Cancel(); }
            catch (Exception exception) { failure = exception; }
            // Recognition/hook failures belong to the individual request task.
            try { await SpeechAsync.WhenAll(pending); } catch { }
            try { DisposeResources(); }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }
            finally { lifetime.Dispose(); }
            if (failure == null) completion.TrySetResult(true);
            else completion.TrySetException(failure);
        }

        /// <summary>Disposes resources owned by this recognizer after all requests finish.</summary>
        protected virtual void DisposeResources() { }

        private void ThrowIfDisposing()
        {
            if (disposing) throw new ObjectDisposedException(GetType().Name);
        }

        private static SpeechRecognizerOptions CopyAndValidate(SpeechRecognizerOptions source)
        {
            var copy = source.Copy();
            if (copy == null) throw new InvalidOperationException("Options.Copy returned null.");
            copy.Validate();
            return copy;
        }

        private static Dictionary<string, object> CopyMetadata(Dictionary<string, object> metadata)
            => metadata == null ? null : new Dictionary<string, object>(metadata);

        private sealed class RequestSnapshot
        {
            public byte[] Audio;
            public SpeechRecognizerOptions Options;
            public Func<string, byte[], CancellationToken, UniTask<SpeechPreprocessResult>> Preprocess;
            public Func<string, string, byte[], Dictionary<string, object>, CancellationToken, UniTask<SpeechPostprocessResult>> Postprocess;
        }
    }
}

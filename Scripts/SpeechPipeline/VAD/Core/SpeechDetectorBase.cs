// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
// See SpeechPipeline/README.ja.md for compatibility and lifecycle decisions.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;

namespace ChatdollKit.SpeechPipeline.VAD
{
    public abstract class SpeechDetectorBase : ISpeechDetector
    {
        protected SpeechDetectorOptions Options;
        protected readonly ISpeechDetectorClock Clock;
        private readonly Dictionary<string, RecordingSession> sessions = new Dictionary<string, RecordingSession>();
        private readonly SpeechAsyncSemaphore operations = new SpeechAsyncSemaphore(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        // One detector instance owns one input lifetime. Ordinary utterance resets
        // do not advance it; explicit audio reset does.
        protected readonly object AudioSync = new object();
        protected long AudioGeneration;
        protected long CurrentInputGeneration;
        private CancellationTokenSource inputCancellation = new CancellationTokenSource();
        protected CancellationToken AudioToken { get { lock (AudioSync) return inputCancellation.Token; } }
        private readonly HashSet<UniTask> background = new HashSet<UniTask>();
        private readonly HashSet<UniTask> streams = new HashSet<UniTask>();
        private readonly SpeechCallbackGuard callbacks = new SpeechCallbackGuard();
        protected SpeechCallbackGuard Callbacks => callbacks;
        private readonly object disposeSync = new object();
        private UniTask disposeTask;
        private volatile bool disposing;
        protected CancellationToken LifetimeToken => lifetime.Token;
        public int SampleRate => Options.SampleRate;
        public int Channels => Options.Channels;
        public event Func<SpeechDetectionResult, UniTask> SpeechDetected;
        public event Func<string, UniTask> RecordingStarted;
        public event Func<string, UniTask> Voiced;
        public event Action<Exception> Error;
        public Func<bool> ShouldMute { get; set; } = () => false;
        public Func<byte[], byte[]> ToLinear16 { get; set; }
        public Func<string, RecordingSession, bool> ShouldTriggerRecordingStarted { get; set; }

        protected SpeechDetectorBase(SpeechDetectorOptions options, ISpeechDetectorClock clock = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();
            Options = options.Copy();
            Clock = clock ?? new SpeechDetectorClock();
        }

        public SpeechDetectorOptions GetOptions() => Options.Copy();

        /// <summary>Updates typed settings. Create a new detector to change its PCM format.
        /// Existing sessions retain their pre-roll capacity and amplitude overrides as in the source.</summary>
        public UniTask UpdateOptionsAsync(SpeechDetectorOptions options, CancellationToken cancellationToken = default)
            => UpdateOptionsCoreAsync(options, false, cancellationToken);

        /// <summary>Applies live configuration to current and future sessions. A changed default volume
        /// replaces all session volume overrides, including disabling the optional Silero volume check.
        /// PCM format, pre-roll capacity and iterator mode require recreating the detector.</summary>
        public UniTask UpdateRuntimeOptionsAsync(SpeechDetectorOptions options, CancellationToken cancellationToken = default)
            => UpdateOptionsCoreAsync(options, true, cancellationToken);

        private UniTask UpdateOptionsCoreAsync(SpeechDetectorOptions options, bool runtime, CancellationToken cancellationToken)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var next = options.Copy();
            next.Validate();
            return WithOperationAsync(() =>
            {
                var previous = Options;
                if (next.GetType() != previous.GetType()) throw new ArgumentException("Options must match the detector type.", nameof(options));
                if (next.SampleRate != previous.SampleRate || next.Channels != previous.Channels)
                    throw new ArgumentException("Create a new detector to change its audio format.", nameof(options));
                if (runtime) ValidateRuntimeOptions(previous, next);
                Options = next;
                OnOptionsUpdated(previous);
                if (runtime) OnRuntimeOptionsUpdated(previous);
                return UniTask.FromResult(true);
            }, cancellationToken);
        }
        protected virtual void OnOptionsUpdated(SpeechDetectorOptions previous) { }
        protected virtual void ValidateRuntimeOptions(SpeechDetectorOptions previous, SpeechDetectorOptions next)
        {
            if (previous.PrerollBufferCount != next.PrerollBufferCount)
                throw new InvalidOperationException("Restart required: recreate the detector to change PrerollBufferCount.");
        }
        protected virtual void OnRuntimeOptionsUpdated(SpeechDetectorOptions previous) { }

        protected void ReplaceSessionVolumeThresholds(double? volumeDbThreshold)
        {
            var amplitude = volumeDbThreshold.HasValue ? DbToAmplitude(volumeDbThreshold.Value) : (double?)null;
            foreach (var session in SessionsCore)
                lock (session.SyncRoot) session.AmplitudeThreshold = amplitude;
        }

        /// <summary>Returns whether recording remains active after processing, not the frame's VAD decision.
        /// Input bytes are copied at entry, so the caller may reuse its capture buffer after this call.</summary>
        public UniTask<bool> ProcessSamplesAsync(byte[] samples, string sessionId = "default", CancellationToken cancellationToken = default)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            ValidateSessionId(sessionId);
            RejectCallbackReentry();
            var ownedSamples = (byte[])samples.Clone();
            lock (AudioSync)
                return SpeechAsync.Share(ProcessAudioAsync(ownedSamples, sessionId, cancellationToken,
                    AudioGeneration, inputCancellation.Token));
        }

        private async UniTask<bool> ProcessAudioAsync(byte[] ownedSamples, string sessionId, CancellationToken token,
            long generation, CancellationToken audioToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, audioToken))
            try
            {
                return await WithOperationAsync(async operationToken =>
                {
                    lock (AudioSync)
                    {
                        if (generation != AudioGeneration) throw new OperationCanceledException(audioToken);
                        CurrentInputGeneration = generation;
                    }
                    var pcm = ToLinear16 == null ? ownedSamples : InvokeExtension(() => ToLinear16(ownedSamples));
                    pcm = InvokeExtension(() => PrepareSamples(pcm, sessionId));
                    if (pcm == null) throw new InvalidOperationException("Audio conversion/filter returned null.");
                    if (pcm.Length % (Channels * 2) != 0) throw new ArgumentException("PCM must contain complete PCM16 frames.", "samples");
                    // Filters may reuse an output buffer on their next call; own retained pre-roll bytes.
                    if (!ReferenceEquals(pcm, ownedSamples)) pcm = (byte[])pcm.Clone();
                    var session = GetSessionCore(sessionId);
                    return await ProcessSamplesCoreAsync(pcm, session, operationToken);
                }, linked.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested &&
                !LifetimeToken.IsCancellationRequested && generation != Interlocked.Read(ref AudioGeneration)) { return false; }
        }

        protected virtual byte[] PrepareSamples(byte[] samples, string sessionId) => samples;
        protected abstract RecordingSession CreateSession(string sessionId);
        protected abstract UniTask<bool> ProcessSamplesCoreAsync(byte[] samples, RecordingSession session, CancellationToken token);

        protected RecordingSession GetSessionCore(string sessionId)
        {
            ValidateSessionId(sessionId);
            if (!sessions.TryGetValue(sessionId, out var session))
            {
                session = CreateSession(sessionId);
                sessions.Add(sessionId, session);
            }
            InitializeSessionThreshold(session);
            return session;
        }

        protected bool TryGetSessionCore(string sessionId, out RecordingSession session)
        {
            ValidateSessionId(sessionId);
            return sessions.TryGetValue(sessionId, out session);
        }

        protected IEnumerable<RecordingSession> SessionsCore => sessions.Values;

        protected virtual void InitializeSessionThreshold(RecordingSession session) { }
        protected static void ValidateSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) throw new ArgumentException("A session ID is required.", nameof(sessionId));
        }

        protected UniTask<T> WithOperationAsync<T>(Func<UniTask<T>> action, CancellationToken token = default)
            => WithOperationAsync(_ => action(), token);

        protected UniTask<T> WithOperationAsync<T>(Func<CancellationToken, UniTask<T>> action, CancellationToken token = default)
            => SpeechAsync.Share(RunOperationAsync(action, token));

        private async UniTask<T> RunOperationAsync<T>(Func<CancellationToken, UniTask<T>> action, CancellationToken token)
        {
            RejectCallbackReentry();
            if (disposing) throw new ObjectDisposedException(GetType().Name);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                await operations.WaitAsync(linked.Token);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (disposing) throw new ObjectDisposedException(GetType().Name);
                    return await action(linked.Token);
                }
                finally { operations.Release(); }
            }
        }

        public UniTask ProcessStreamAsync(IAsyncEnumerable<byte[]> stream, string sessionId = "default", CancellationToken cancellationToken = default)
        {
            RejectCallbackReentry();
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            ValidateSessionId(sessionId);
            lock (disposeSync)
            {
                if (disposing) throw new ObjectDisposedException(GetType().Name);
                var completion = new SpeechCompletionSource<bool>();
                var task = completion.Task.AsUniTask();
                lock (streams) streams.Add(task);
                TrackStreamAsync(stream, sessionId, cancellationToken, completion, task).Forget();
                return task;
            }
        }

        private async UniTask TrackStreamAsync(IAsyncEnumerable<byte[]> stream, string sessionId,
            CancellationToken token, SpeechCompletionSource<bool> completion, UniTask tracked)
        {
            try { await ProcessStreamCoreAsync(stream, sessionId, token); completion.TrySetResult(true); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { lock (streams) streams.Remove(tracked); }
        }

        private async UniTask ProcessStreamCoreAsync(IAsyncEnumerable<byte[]> stream, string sessionId, CancellationToken cancellationToken)
        {
            await SpeechAsync.Yield(); // Register the stream before user code runs, even for synchronous enumerators.
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                IAsyncEnumerator<byte[]> iterator = null;
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    iterator = InvokeExtension(() => stream.GetAsyncEnumerator(linked.Token));
                    while (await InvokeExtensionAsync(async () => await iterator.MoveNextAsync()))
                    {
                        if (iterator.Current == null || iterator.Current.Length == 0) break;
                        await ProcessSamplesAsync(iterator.Current, sessionId, linked.Token);
                        await SpeechAsync.Yield(); // Cooperatively process finite in-memory and live streams alike.
                    }
                }
                finally
                {
                    try
                    {
                        if (iterator != null)
                            await InvokeExtensionAsync(async () => { await iterator.DisposeAsync(); return true; });
                    }
                    finally
                    {
                        if (!disposing)
                        {
                            try { await FinalizeSessionAsync(sessionId); }
                            catch (ObjectDisposedException) when (disposing) { }
                            catch (OperationCanceledException) when (disposing) { }
                        }
                    }
                }
            }
        }

        public UniTask ResetSessionAsync(string sessionId = "default", CancellationToken cancellationToken = default) => WithOperationAsync(() =>
        {
            ValidateSessionId(sessionId);
            if (sessions.TryGetValue(sessionId, out var session)) ResetSessionCore(session);
            return UniTask.FromResult(true);
        }, cancellationToken);

        protected virtual void ResetSessionCore(RecordingSession session)
        {
            lock (session.SyncRoot) session.Reset();
        }

        public UniTask ResetSpeechInputAsync(string sessionId = "default", bool clearPreroll = true, CancellationToken cancellationToken = default)
        {
            ValidateSessionId(sessionId);
            RejectCallbackReentry();
            cancellationToken.ThrowIfCancellationRequested();
            if (disposing) throw new ObjectDisposedException(GetType().Name);
            CancellationTokenSource previous;
            UniTask reset;
            lock (AudioSync)
            {
                previous = inputCancellation;
                inputCancellation = new CancellationTokenSource();
                AudioGeneration++;
                // Enqueue cleanup before new input; actual model inference remains serialized.
                reset = WithOperationAsync(() =>
                {
                    if (sessions.TryGetValue(sessionId, out var session)) ResetSpeechInputCore(session, clearPreroll);
                    return UniTask.FromResult(true);
                }).AsUniTask();
            }
            try { callbacks.Invoke(previous.Cancel); }
            catch (Exception error) { ReportError(error is AggregateException aggregate ? aggregate.Flatten() : error); }
            finally { previous.Dispose(); }
            return reset;
        }

        protected virtual void ResetSpeechInputCore(RecordingSession session, bool clearPreroll)
        {
            ResetSessionCore(session);
            lock (session.SyncRoot) if (clearPreroll) session.PrerollBuffer.Clear();
        }

        public UniTask FinalizeSessionAsync(string sessionId = "default", CancellationToken cancellationToken = default) => WithOperationAsync(() =>
        {
            ValidateSessionId(sessionId);
            if (sessions.TryGetValue(sessionId, out var session))
            {
                DeleteSessionCore(session);
                sessions.Remove(sessionId);
            }
            else OnSessionDeleted(sessionId);
            return UniTask.FromResult(true);
        }, cancellationToken);

        protected virtual void DeleteSessionCore(RecordingSession session)
        {
            ResetSessionCore(session);
            OnSessionDeleted(session.SessionId);
        }
        protected virtual void OnSessionDeleted(string sessionId) { }

        public UniTask<object> GetSessionDataAsync(string sessionId, string key, CancellationToken cancellationToken = default) => WithOperationAsync(() =>
        {
            ValidateSessionId(sessionId);
            object result = null;
            if (sessions.TryGetValue(sessionId, out var session)) lock (session.SyncRoot) session.Data.TryGetValue(key, out result);
            return UniTask.FromResult(result);
        }, cancellationToken);

        public UniTask SetSessionDataAsync(string sessionId, string key, object value, bool createSession = false, CancellationToken cancellationToken = default) => WithOperationAsync(() =>
        {
            ValidateSessionId(sessionId);
            RecordingSession session;
            if (createSession) session = GetSessionCore(sessionId);
            else sessions.TryGetValue(sessionId, out session);
            if (session != null) lock (session.SyncRoot) session.Data[key] = value;
            return UniTask.FromResult(true);
        }, cancellationToken);

        public UniTask<bool> IsRecordingAsync(string sessionId = "default", CancellationToken cancellationToken = default) => WithOperationAsync(() =>
        {
            ValidateSessionId(sessionId);
            return UniTask.FromResult(sessions.TryGetValue(sessionId, out var session) && session.IsRecording);
        }, cancellationToken);

        public UniTask SetVolumeDbThresholdAsync(string sessionId, double value, CancellationToken cancellationToken = default) => WithOperationAsync(() =>
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            var session = GetSessionCore(sessionId);
            lock (session.SyncRoot) session.AmplitudeThreshold = DbToAmplitude(value);
            return UniTask.FromResult(true);
        }, cancellationToken);

        protected static double DbToAmplitude(double db) => 32767 * Math.Pow(10, db / 20);
        protected static int MaxAmplitude(byte[] samples)
        {
            if (samples.Length == 0) throw new ArgumentException("Amplitude VAD requires non-empty PCM.", nameof(samples));
            var maximum = 0;
            for (var i = 0; i < samples.Length; i += 2)
            {
                var value = Math.Abs((int)(short)(samples[i] | samples[i + 1] << 8));
                if (value > maximum) maximum = value;
            }
            return maximum;
        }
        protected double SampleDuration(byte[] samples) => samples.Length / (2.0 * SampleRate * Channels);

        protected async UniTask NotifyVoicedAsync(string sessionId)
        {
            var handlers = Voiced;
            if (handlers == null) return;
            foreach (Func<string, UniTask> handler in handlers.GetInvocationList())
                await InvokeCallbackAsync(() => handler(sessionId));
        }

        protected void CheckRecordingStarted(RecordingSession session, string text = null)
        {
            var handlers = RecordingStarted;
            if (handlers == null) return;
            lock (session.SyncRoot)
            {
                if (session.RecordingStartedTriggered) return;
                var checkText = text ?? session.LastRecognizedText;
                var shouldTrigger = ShouldTriggerRecordingStarted != null
                    ? InvokeExtension(() => ShouldTriggerRecordingStarted(checkText, session))
                    : session.RecordDuration - session.SilenceDuration >= Options.RecordingStartedMinDuration ||
                      (!string.IsNullOrEmpty(checkText) && CodePointLength(checkText) >= Options.RecordingStartedMinTextLength);
                if (!shouldTrigger) return;
                session.RecordingStartedTriggered = true;
            }
            foreach (Func<string, UniTask> handler in handlers.GetInvocationList())
                TrackBackground(() => InvokeCallbackAsync(() => handler(session.SessionId)));
        }

        private static int CodePointLength(string text)
        {
            var count = 0;
            for (var i = 0; i < text.Length; i++, count++)
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            return count;
        }

        protected void PublishSpeechDetected(SpeechDetectionResult result)
        {
            var handlers = SpeechDetected;
            if (handlers == null) return;
            TrackBackground(async () =>
            {
                foreach (Func<SpeechDetectionResult, UniTask> handler in handlers.GetInvocationList())
                    await InvokeCallbackAsync(() => handler(result));
            });
        }

        protected async UniTask InvokeCallbackAsync(Func<UniTask> callback)
        {
            try { await callbacks.InvokeAsync(callback); }
            catch (OperationCanceledException) when (LifetimeToken.IsCancellationRequested) { }
            catch (Exception ex) { ReportError(ex); }
        }

        protected T InvokeExtension<T>(Func<T> extension) => callbacks.Invoke(extension);

        protected UniTask<T> InvokeExtensionAsync<T>(Func<UniTask<T>> extension) => callbacks.InvokeAsync(extension);

        protected void ReportError(Exception exception)
        {
            var handlers = Error;
            if (handlers == null) return;
            foreach (Action<Exception> handler in handlers.GetInvocationList())
                try { callbacks.Invoke(() => handler(exception)); }
                catch { /* Error observers must not fault audio processing. */ }
        }

        protected UniTask TrackBackground(Func<UniTask> work)
        {
            var completion = new SpeechCompletionSource<bool>();
            var task = completion.Task.AsUniTask();
            lock (background) background.Add(task);
            RunBackgroundAsync(work, completion, task).Forget();
            return task;
        }

        private async UniTask RunBackgroundAsync(Func<UniTask> work, SpeechCompletionSource<bool> completion, UniTask tracked)
        {
            await SpeechAsync.Yield(); // Defer work so its task is registered before callbacks run.
            try { await work(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ReportError(ex); }
            finally
            {
                lock (background) background.Remove(tracked);
                completion.TrySetResult(true);
            }
        }

        public virtual async UniTask DrainAsync()
        {
            RejectCallbackReentry();
            while (true)
            {
                UniTask[] pending;
                lock (background)
                {
                    background.RemoveWhere(task => task.Status.IsCompleted());
                    pending = background.ToArray();
                }
                if (pending.Length == 0) return;
                await SpeechAsync.WhenAll(pending);
            }
        }

        private void RejectCallbackReentry()
        {
            callbacks.ThrowIfActive("Schedule detector control after the callback returns; awaiting detector operations inside its callbacks can deadlock.");
        }

        public UniTask DisposeAsync()
        {
            RejectCallbackReentry();
            lock (disposeSync)
            {
                if (disposing) return disposeTask;
                disposing = true;
                var completion = new SpeechCompletionSource<bool>();
                disposeTask = completion.Task.AsUniTask();
                CompleteDisposalAsync(completion).Forget();
                return disposeTask;
            }
        }

        private async UniTask CompleteDisposalAsync(SpeechCompletionSource<bool> completion)
        {
            try { await DisposeCoreAsync(); completion.TrySetResult(true); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
        }

        private async UniTask DisposeCoreAsync()
        {
            try { lifetime.Cancel(); } catch (Exception ex) { ReportError(ex); }
            await operations.WaitAsync();
            try
            {
                foreach (var session in sessions.Values)
                    try { DeleteSessionCore(session); } catch (Exception ex) { ReportError(ex); }
                sessions.Clear();
            }
            finally { operations.Release(); }
            UniTask[] activeStreams;
            lock (streams) activeStreams = streams.ToArray();
            try { await SpeechAsync.WhenAll(activeStreams); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ReportError(ex); }
            await DrainAsync();
            inputCancellation.Dispose();
            // Injected models, recognizers and filters remain caller-owned.
        }
    }
}

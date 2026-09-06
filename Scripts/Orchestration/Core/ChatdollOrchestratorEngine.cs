using ChatdollKit.SpeechPipeline.Remote;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline;

namespace ChatdollKit.Orchestration
{
    /// <summary>
    /// Coordinates a single pipeline with device input and avatar presentation, independently of Unity.
    /// Notifications run outside pipeline callbacks. Subscribers must not block a notification thread.
    /// Stop input before DrainAsync when a continuously recording microphone is attached.
    /// </summary>
    public sealed class ChatdollOrchestratorEngine
    {
        private sealed class Turn
        {
            internal string Id;
            internal long Order;
            internal bool Started, Terminal, Stopped, BlockInput;
            internal int Pending;
            internal readonly CancellationTokenSource Source = new CancellationTokenSource();
        }
        private sealed class Playback
        {
            internal Turn Turn;
            internal AvatarRequest Request;
            internal SpeechCompletionSource<bool> StopCompletion;
        }
        private sealed class Invocation
        {
            internal CancellationTokenSource Source;
            internal UniTask PreviousSubmission;
            internal readonly SpeechCompletionSource<bool> Submitted = CompletionSource<bool>();
            internal readonly SpeechCompletionSource<SpeechPipelineResponse> Completion = CompletionSource<SpeechPipelineResponse>();
        }

        private readonly object sync = new object();
        private readonly SpeechCallbackGuard requestCallbacks;
        private readonly ISpeechPipeline pipeline;
        private readonly IAvatarController avatar;
        private readonly bool ownsPipeline;
        private ChatdollOrchestratorOptions options;
        private readonly Dictionary<string, Turn> turns = new Dictionary<string, Turn>();
        private readonly Dictionary<string, long> startOrders = new Dictionary<string, long>();
        private readonly HashSet<Invocation> invocations = new HashSet<Invocation>();
        private readonly LinkedList<Playback> playbackQueue = new LinkedList<Playback>();
        private readonly Queue<byte[]> audioQueue = new Queue<byte[]>();
        private readonly Queue<Action> notifications = new Queue<Action>();
        private readonly SpeechAsyncSemaphore playbackSignal = new SpeechAsyncSemaphore(0);
        private readonly SpeechAsyncSemaphore audioSignal = new SpeechAsyncSemaphore(0);
        private readonly SpeechAsyncSemaphore notificationSignal = new SpeechAsyncSemaphore(0);
        private readonly UniTask playbackWorker, audioWorker, notificationWorker;
        private SpeechCompletionSource<bool> playbackIdle = CompletedSource(), audioIdle = CompletedSource(), notificationIdle = CompletedSource();
        private Playback playing;
        private CancellationTokenSource activeAudio;
        private bool inputEnabled = true, inputFaulted, inputSuppressed;
        private bool controlling, disposing, notificationsClosing;
        private long nextStartOrder;
        private string lastStartedId;
        private UniTask controlTask;
        private UniTask? disposalTask;
        private UniTask lastSubmission = UniTask.CompletedTask;

        public event Action<SpeechPipelineResponse> ResponseReceived;
        public event Action<AvatarRequest> PresentationStarted;
        public event Action<AvatarRequest> PresentationCompleted;
        public event Action<Exception> Error;
        public event Action InputSuppressionChanged;

        /// <summary>Optional application customization before submitting a text/image/audio request.</summary>
        public Func<SpeechPipelineRequest, CancellationToken, UniTask> BeforeRequestAsync { get; set; }
        public string SessionId => pipeline.SessionId;
        public bool IsPresenting { get { lock (sync) return playing?.Request != null; } }
        public bool IsInputSuppressed { get { lock (sync) return inputSuppressed; } }
        /// <summary>Controls forwarding only. Use InterruptAsync to also discard already submitted VAD/recognition work.</summary>
        public bool InputEnabled
        {
            get { lock (sync) return inputEnabled; }
            set { lock (sync) { CheckAlive(); inputEnabled = value; UpdateSuppressionLocked(); } }
        }
        /// <summary>Changes microphone forwarding during active responses, including pending avatar playback.</summary>
        public bool AllowBargeIn
        {
            get { lock (sync) return options.AllowBargeIn; }
            set { lock (sync) { CheckAlive(); options.AllowBargeIn = value; UpdateSuppressionLocked(); } }
        }

        public ChatdollOrchestratorEngine(ISpeechPipeline pipeline, IAvatarController avatar,
            ChatdollOrchestratorOptions options = null, bool ownsPipeline = false)
            : this(pipeline, avatar, options, ownsPipeline, new SpeechCallbackGuard()) { }

        internal ChatdollOrchestratorEngine(ISpeechPipeline pipeline, IAvatarController avatar,
            ChatdollOrchestratorOptions options, bool ownsPipeline, SpeechCallbackGuard callbackGuard)
        {
            requestCallbacks = callbackGuard ?? throw new ArgumentNullException(nameof(callbackGuard));
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            this.avatar = avatar ?? throw new ArgumentNullException(nameof(avatar));
            this.options = (options ?? new ChatdollOrchestratorOptions()).Copy();
            this.options.Validate();
            this.ownsPipeline = ownsPipeline;
            pipeline.ResponseReceived += OnResponseAsync;
            pipeline.Error += ReportError;
            playbackWorker = Detached(PlaybackLoopAsync);
            audioWorker = Detached(AudioLoopAsync);
            notificationWorker = Detached(NotificationLoopAsync);
        }

        public ChatdollOrchestratorOptions GetOptions() { lock (sync) return options.Copy(); }
        public void UpdateOptions(ChatdollOrchestratorOptions replacement)
        {
            var copy = replacement?.Copy() ?? throw new ArgumentNullException(nameof(replacement));
            copy.Validate();
            lock (sync)
            {
                CheckAvailable();
                options = copy; UpdateSuppressionLocked();
            }
        }

        /// <summary>Copies one mono PCM16 frame into a bounded queue. False means the frame was not accepted.</summary>
        public bool SubmitAudio(byte[] samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if ((samples.Length & 1) != 0) throw new ArgumentException("PCM16 input must contain complete samples.", nameof(samples));
            lock (sync)
            {
                if (inputSuppressed || disposing) return false;
                if (samples.Length == 0) return true;
                if (audioQueue.Count >= options.MaxPendingAudioFrames)
                {
                    inputFaulted = true;
                    UpdateSuppressionLocked();
                    ReportErrorLocked(new InvalidOperationException("Microphone input queue overflowed. Pending audio was discarded; resume input explicitly after addressing the producer rate."));
                    return false;
                }
                if (audioIdle.Task.Status.IsCompleted()) audioIdle = CompletionSource<bool>();
                audioQueue.Enqueue((byte[])samples.Clone()); audioSignal.Release();
                return true;
            }
        }

        public void ResumeAudioInput()
        {
            lock (sync) { CheckAvailable(); inputFaulted = false; UpdateSuppressionLocked(); }
        }

        public UniTask<SpeechPipelineResponse> SendTextAsync(string text, CancellationToken cancellationToken = default) =>
            InvokeAsync(new SpeechPipelineRequest { Text = text }, cancellationToken);

        public UniTask<SpeechPipelineResponse> InvokeAsync(SpeechPipelineRequest request, CancellationToken cancellationToken = default)
        {
            RejectHookReentry();
            var copy = request?.Copy() ?? throw new ArgumentNullException(nameof(request));
            Invocation invocation;
            lock (sync)
            {
                CheckAvailable(); cancellationToken.ThrowIfCancellationRequested();
                invocation = new Invocation
                {
                    Source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
                    PreviousSubmission = lastSubmission
                };
                // A canceled queued request cannot let later requests overtake its still-preparing predecessor.
                lastSubmission = SpeechAsync.Share(UniTask.WhenAll(invocation.PreviousSubmission, invocation.Submitted.Task));
                invocations.Add(invocation);
            }
            _ = Detached(() => InvokeCoreAsync(copy, invocation));
            return invocation.Completion.Task;
        }

        private async UniTask InvokeCoreAsync(SpeechPipelineRequest request, Invocation invocation)
        {
            try
            {
                var token = invocation.Source.Token;
                UniTask<SpeechPipelineResponse> response;
                try
                {
                    // Preserve submission order even when application preparation is asynchronous.
                    // Only registration is serialized; the pipeline still owns generation scheduling.
                    await WaitAsync(invocation.PreviousSubmission, token);
                    token.ThrowIfCancellationRequested();
                    var before = BeforeRequestAsync;
                    if (before != null)
                    {
                        await requestCallbacks.InvokeAsync(() => before(request, token));
                    }
                    lock (sync)
                    {
                        if (controlling || disposing) throw new OperationCanceledException(token);
                        token.ThrowIfCancellationRequested();
                        response = pipeline.InvokeAsync(request, token);
                    }
                }
                finally { invocation.Submitted.TrySetResult(true); }
                var result = await response;
                invocation.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { invocation.Completion.TrySetCanceled(); }
            catch (Exception error) { ReportError(error); invocation.Completion.TrySetException(error); }
            finally { lock (sync) { invocations.Remove(invocation); invocation.Source.Dispose(); } }
        }

        // This callback does not await presentation or application hooks and never reenters the pipeline.
        private UniTask OnResponseAsync(SpeechPipelineResponse response)
        {
            if (response == null || (response.SessionId != null && response.SessionId != SessionId)) return UniTask.CompletedTask;
            var copy = response.Copy();
            var cancel = new List<CancellationTokenSource>();
            lock (sync)
            {
                if (disposing || controlling) return UniTask.CompletedTask;
                Turn turn = null;
                if (copy.TransactionId != null) turns.TryGetValue(copy.TransactionId, out turn);
                switch (copy.Type)
                {
                    case SpeechPipelineResponseType.Accepted:
                        if (string.IsNullOrEmpty(copy.TransactionId) || turn != null) return UniTask.CompletedTask;
                        turn = new Turn { Id = copy.TransactionId, BlockInput = copy.Metadata?.Value<bool?>("block_barge_in") == true };
                        turns.Add(turn.Id, turn);
                        break;
                    case SpeechPipelineResponseType.Start:
                        if (turn == null || turn.Stopped || turn.Terminal || turn.Started) return UniTask.CompletedTask;
                        turn.Started = true; turn.Order = ++nextStartOrder;
                        startOrders[turn.Id] = turn.Order; lastStartedId = turn.Id;
                        break;
                    case SpeechPipelineResponseType.Chunk:
                        if (turn == null || !turn.Started || turn.Terminal || turn.Stopped) return UniTask.CompletedTask;
                        if (playbackQueue.Count(item => item.Request != null) >= options.MaxPendingPresentations)
                        {
                            StopTurnsLocked(item => ReferenceEquals(item, turn), cancel);
                            ReportErrorLocked(new InvalidOperationException("Avatar presentation queue overflowed. The affected turn was discarded."));
                            break;
                        }
                        try
                        {
                            var request = CreateAvatarRequest(copy);
                            turn.Pending++;
                            EnqueuePlaybackLocked(new Playback { Turn = turn, Request = request });
                        }
                        catch (Exception error)
                        {
                            StopTurnsLocked(item => ReferenceEquals(item, turn), cancel);
                            ReportErrorLocked(error);
                        }
                        break;
                    case SpeechPipelineResponseType.Stop:
                        long cutoff;
                        if (copy.TransactionId == null) cutoff = nextStartOrder;
                        else if (!startOrders.TryGetValue(copy.TransactionId, out cutoff)) return UniTask.CompletedTask;
                        StopTurnsLocked(item => item.Started && item.Order <= cutoff, cancel);
                        break;
                    case SpeechPipelineResponseType.Canceled:
                    case SpeechPipelineResponseType.Error:
                        if (turn != null)
                        {
                            turn.Terminal = true;
                            StopTurnsLocked(item => ReferenceEquals(item, turn), cancel);
                        }
                        break;
                    case SpeechPipelineResponseType.Final:
                        if (turn != null) { turn.Terminal = true; CleanupTurnLocked(turn); }
                        break;
                    case SpeechPipelineResponseType.ToolCall:
                        if (turn == null || turn.Stopped || turn.Terminal) return UniTask.CompletedTask;
                        break;
                }
                UpdateSuppressionLocked();
                NotifyLocked(() => Publish(ResponseReceived, copy, item => item.Copy()));
            }
            foreach (var source in cancel) Cancel(source);
            return UniTask.CompletedTask;
        }

        private void StopTurnsLocked(Func<Turn, bool> predicate, List<CancellationTokenSource> cancel)
        {
            var affected = turns.Values.Where(predicate).ToArray();
            foreach (var turn in affected) { turn.Stopped = true; cancel.Add(turn.Source); }
            for (var node = playbackQueue.First; node != null;)
            {
                var next = node.Next;
                if (node.Value.Turn != null && predicate(node.Value.Turn))
                {
                    node.Value.Turn.Pending--; playbackQueue.Remove(node);
                }
                node = next;
            }
            // An old stop must not stop a newer presentation. The fence precedes all remaining queued work.
            if (playing == null || (playing.Turn != null && predicate(playing.Turn)))
                EnqueuePlaybackLocked(new Playback { StopCompletion = CompletionSource<bool>() }, first: true);
            foreach (var turn in affected) CleanupTurnLocked(turn);
        }

        private void EnqueuePlaybackLocked(Playback item, bool first = false)
        {
            if (playbackIdle.Task.Status.IsCompleted()) playbackIdle = CompletionSource<bool>();
            if (first) playbackQueue.AddFirst(item); else playbackQueue.AddLast(item);
            // Response-triggered stop fences have no public waiter; errors are also sent to Error.
            if (item.StopCompletion != null)
                _ = SettleAsync(new UniTask[] { item.StopCompletion.Task });
            playbackSignal.Release();
        }

        private async UniTask PlaybackLoopAsync()
        {
            while (true)
            {
                await playbackSignal.WaitAsync();
                Playback item;
                lock (sync)
                {
                    if (playbackQueue.Count == 0) { if (disposing) return; continue; }
                    item = playbackQueue.First.Value; playbackQueue.RemoveFirst(); playing = item;
                }
                try
                {
                    if (item.StopCompletion != null)
                    {
                        await avatar.StopAsync();
                        item.StopCompletion.TrySetResult(true);
                    }
                    else
                    {
                        var token = item.Turn.Source.Token;
                        token.ThrowIfCancellationRequested();
                        lock (sync) NotifyLocked(() => Publish(PresentationStarted, item.Request, request => request.Copy()));
                        await avatar.PresentAsync(item.Request.Copy(), token);
                        token.ThrowIfCancellationRequested();
                        lock (sync) NotifyLocked(() => Publish(PresentationCompleted, item.Request, request => request.Copy()));
                    }
                }
                catch (OperationCanceledException) when (item.Turn?.Source.IsCancellationRequested == true) { }
                catch (Exception error)
                {
                    item.StopCompletion?.TrySetException(error);
                    ReportError(error);
                    if (item.Turn != null)
                    {
                        var cancel = new List<CancellationTokenSource>();
                        lock (sync) StopTurnsLocked(turn => ReferenceEquals(turn, item.Turn), cancel);
                        foreach (var source in cancel) Cancel(source);
                    }
                }
                finally
                {
                    lock (sync)
                    {
                        playing = null;
                        if (item.Turn != null) { item.Turn.Pending--; CleanupTurnLocked(item.Turn); }
                        if (playbackQueue.Count == 0) playbackIdle.TrySetResult(true);
                        UpdateSuppressionLocked();
                        if (disposing) playbackSignal.Release();
                    }
                }
            }
        }

        private async UniTask AudioLoopAsync()
        {
            while (true)
            {
                await audioSignal.WaitAsync();
                byte[] frame;
                CancellationTokenSource source;
                lock (sync)
                {
                    if (audioQueue.Count == 0) { if (disposing) return; continue; }
                    frame = audioQueue.Dequeue(); activeAudio = source = new CancellationTokenSource();
                }
                try { await pipeline.ProcessAudioSamplesAsync(frame, source.Token); }
                catch (OperationCanceledException) when (source.IsCancellationRequested) { }
                catch (Exception error)
                {
                    lock (sync) { inputFaulted = true; UpdateSuppressionLocked(); ReportErrorLocked(error); }
                }
                finally
                {
                    lock (sync)
                    {
                        activeAudio = null; source.Dispose();
                        if (audioQueue.Count == 0) audioIdle.TrySetResult(true);
                        if (disposing) audioSignal.Release();
                    }
                }
            }
        }

        public UniTask InterruptAsync(CancellationToken cancellationToken = default) => BeginControl(null, false, cancellationToken);
        public UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default) => BeginControl(contextId, true, cancellationToken);

        private UniTask BeginControl(string contextId, bool reset, CancellationToken token)
        {
            RejectHookReentry();
            token.ThrowIfCancellationRequested();
            SpeechCompletionSource<bool> completion;
            List<CancellationTokenSource> cancel;
            UniTask stop;
            lock (sync)
            {
                CheckAvailable(); controlling = true;
                completion = CompletionSource<bool>(); controlTask = completion.Task;
                cancel = invocations.Select(item => item.Source).ToList();
                if (activeAudio != null) cancel.Add(activeAudio);
                StopTurnsLocked(item => true, cancel);
                stop = EnqueueStopBarrierLocked();
                UpdateSuppressionLocked();
            }
            foreach (var source in cancel) Cancel(source);
            _ = Detached(() => ControlCoreAsync(contextId, reset, stop, completion));
            return WaitAsync(completion.Task, token);
        }

        private async UniTask ControlCoreAsync(string contextId, bool reset, UniTask stop, SpeechCompletionSource<bool> completion)
        {
            Exception failure = null;
            try
            {
                // Once accepted, a caller canceling its wait must not reopen input before the boundary completes.
                try
                {
                    if (reset) await pipeline.ResetAsync(contextId);
                    else await pipeline.InterruptAsync();
                }
                catch (Exception error) { failure = error; ReportError(error); }
                UniTask[] pending;
                lock (sync) pending = invocations.Select(item => (UniTask)item.Completion.Task).Append(audioIdle.Task).ToArray();
                await SettleAsync(pending);
                await stop;
            }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); ReportError(error); }
            finally
            {
                lock (sync)
                {
                    controlling = false; controlTask = default;
                    // A failed control boundary needs an explicit retry before more microphone data is accepted.
                    if (failure != null) inputFaulted = true;
                    UpdateSuppressionLocked();
                    if (failure == null) completion.TrySetResult(true); else completion.TrySetException(failure);
                }
            }
        }

        private UniTask EnqueueStopBarrierLocked()
        {
            var barrier = CompletionSource<bool>();
            EnqueuePlaybackLocked(new Playback { StopCompletion = barrier });
            return barrier.Task;
        }

        public async UniTask DrainAsync()
        {
            RejectHookReentry();
            while (true)
            {
                UniTask input, control;
                UniTask[] pending;
                lock (sync) { CheckAlive(); input = audioIdle.Task; control = controlTask; pending = invocations.Select(item => (UniTask)item.Completion.Task).ToArray(); }
                await input;
                await control;
                await SettleAsync(pending);
                await pipeline.DrainAsync();
                UniTask playback, notification;
                lock (sync) playback = playbackIdle.Task;
                await playback;
                lock (sync) notification = notificationIdle.Task;
                await notification;
                lock (sync)
                    if (audioIdle.Task.Status.IsCompleted() && playbackIdle.Task.Status.IsCompleted() && notificationIdle.Task.Status.IsCompleted() &&
                        invocations.Count == 0 && !controlling) return;
            }
        }

        public UniTask DisposeAsync()
        {
            RejectHookReentry();
            SpeechCompletionSource<bool> completion;
            List<CancellationTokenSource> cancel;
            UniTask control;
            lock (sync)
            {
                if (disposalTask.HasValue) return disposalTask.Value;
                completion = CompletionSource<bool>(); disposalTask = completion.Task;
                disposing = true; control = controlTask;
                cancel = invocations.Select(item => item.Source).ToList();
                if (activeAudio != null) cancel.Add(activeAudio);
                StopTurnsLocked(item => true, cancel);
                EnqueueStopBarrierLocked();
                UpdateSuppressionLocked(); audioSignal.Release(); playbackSignal.Release();
            }
            pipeline.ResponseReceived -= OnResponseAsync;
            foreach (var source in cancel) Cancel(source);
            _ = Detached(() => DisposeCoreAsync(control, completion));
            return completion.Task;
        }

        private async UniTask DisposeCoreAsync(UniTask control, SpeechCompletionSource<bool> completion)
        {
            var failures = new List<Exception>();
            try { await control; } catch (Exception error) { failures.Add(error); }
            try
            {
                if (ownsPipeline) await requestCallbacks.InvokeAsync(pipeline.DisposeAsync);
                else await requestCallbacks.InvokeAsync(() => pipeline.InterruptAsync());
            }
            catch (Exception error) { failures.Add(error); }
            UniTask[] pending;
            lock (sync) pending = invocations.Select(item => (UniTask)item.Completion.Task).ToArray();
            await SettleAsync(pending);
            foreach (var worker in new[] { playbackWorker, audioWorker })
                try { await worker; } catch (Exception error) { failures.Add(error); }
            pipeline.Error -= ReportError;
            lock (sync) { notificationsClosing = true; notificationSignal.Release(); }
            await notificationWorker;
            lock (sync)
            {
                foreach (var turn in turns.Values) turn.Source.Dispose();
                turns.Clear(); startOrders.Clear();
                audioSignal.Dispose(); playbackSignal.Dispose(); notificationSignal.Dispose();
            }
            if (failures.Count == 0) completion.TrySetResult(true); else completion.TrySetException(new AggregateException(failures));
        }

        private void CleanupTurnLocked(Turn turn)
        {
            if (turn.Pending == 0 && (turn.Terminal || turn.Stopped))
            {
                turns.Remove(turn.Id); turn.Source.Dispose();
            }
            // Retain stopped-generation cutoffs while any earlier audio still exists, including after Final.
            var pending = turns.Values.Where(item => item.Started && item.Pending > 0).Select(item => item.Order);
            var minimum = pending.DefaultIfEmpty(nextStartOrder + 1).Min();
            foreach (var id in startOrders.Where(item => item.Value < minimum && item.Key != lastStartedId).Select(item => item.Key).ToArray())
                startOrders.Remove(id);
        }

        private void UpdateSuppressionLocked()
        {
            var suppressed = !inputEnabled || inputFaulted || controlling || disposing ||
                turns.Values.Any(turn => !turn.Stopped && (!turn.Terminal || turn.Pending > 0) &&
                    (turn.BlockInput || !options.AllowBargeIn));
            if (suppressed == inputSuppressed) return;
            inputSuppressed = suppressed;
            if (suppressed)
            {
                audioQueue.Clear();
                if (activeAudio == null) audioIdle.TrySetResult(true);
            }
            NotifyLocked(() => Publish(InputSuppressionChanged));
        }

        private void NotifyLocked(Action action)
        {
            if (notificationsClosing) return;
            if (notificationIdle.Task.Status.IsCompleted()) notificationIdle = CompletionSource<bool>();
            notifications.Enqueue(action); notificationSignal.Release();
        }
        private async UniTask NotificationLoopAsync()
        {
            while (true)
            {
                await notificationSignal.WaitAsync();
                Action action;
                lock (sync)
                {
                    if (notifications.Count == 0) { if (notificationsClosing) return; continue; }
                    action = notifications.Dequeue();
                }
                try { action(); } catch (Exception error) { ReportError(error); }
                lock (sync) if (notifications.Count == 0) notificationIdle.TrySetResult(true);
            }
        }
        private void Publish(Action handlers)
        {
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch (Exception error) { ReportError(error); }
        }
        private void Publish<T>(Action<T> handlers, T value, Func<T, T> copy)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList()) try { handler(copy(value)); } catch (Exception error) { ReportError(error); }
        }
        private void ReportError(Exception error) { lock (sync) ReportErrorLocked(error); }
        private void ReportErrorLocked(Exception error) => NotifyLocked(() =>
        {
            var handlers = Error;
            if (handlers != null) foreach (Action<Exception> handler in handlers.GetInvocationList()) try { handler(error); } catch { }
        });
        private void Cancel(CancellationTokenSource source)
        {
            try { source.Cancel(); } catch (ObjectDisposedException) { } catch (Exception error) { ReportError(error); }
        }
        private void CheckAlive() { if (disposing) throw new ObjectDisposedException(nameof(ChatdollOrchestratorEngine)); }
        private void RejectHookReentry()
        {
            requestCallbacks.ThrowIfActive("Do not invoke requests or await control/drain/disposal from BeforeRequestAsync. Customize the supplied request instead.");
        }
        private void CheckAvailable()
        {
            CheckAlive();
            if (controlling) throw new InvalidOperationException("Wait for the current interrupt/reset operation to complete.");
        }
        // Resolve pipeline-specific tags once; the queued avatar request owns its audio.
        private static AvatarRequest CreateAvatarRequest(SpeechPipelineResponse response) => new AvatarRequest
        {
            SessionId = response.SessionId,
            ContextId = response.ContextId,
            TransactionId = response.TransactionId,
            Text = response.Text,
            VoiceText = response.VoiceText,
            Language = response.Language,
            AudioData = (byte[])response.AudioData?.Clone(),
            Controls = AvatarControlParser.Parse(response.Text, response.Metadata)
        };
        private static SpeechCompletionSource<T> CompletionSource<T>() => new SpeechCompletionSource<T>();
        private static SpeechCompletionSource<bool> CompletedSource() { var result = CompletionSource<bool>(); result.SetResult(true); return result; }
        private static UniTask Detached(Func<UniTask> operation) => SpeechAsync.Run(operation);
        private static async UniTask SettleAsync(IEnumerable<UniTask> tasks)
        {
            foreach (var task in tasks) try { await task; } catch { }
        }
        private static UniTask WaitAsync(UniTask task, CancellationToken token) => SpeechAsync.WaitAsync(task, token);
    }
}

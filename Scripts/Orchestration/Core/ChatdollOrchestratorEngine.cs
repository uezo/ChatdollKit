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
            internal string SessionId, ContextId;
            internal long Order, Generation;
            internal bool Started, Terminal, Stopped, BlockInput, Ended;
            internal OrchestratorTurnEndReason EndReason;
            internal int Pending;
            internal readonly CancellationTokenSource Source = new CancellationTokenSource();
        }
        private sealed class Playback
        {
            internal Turn Turn;
            internal CancellationTokenSource BackchannelSource;
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
        private long nextStartOrder, generation;
        private string lastStartedId;
        private UniTask controlTask;
        private UniTask? disposalTask;
        private UniTask lastSubmission = UniTask.CompletedTask;
        private volatile bool debugLogEnabled;
        private string debugTransactionId;
        private long debugQueuedFrames, debugSentFrames, debugDroppedFrames, debugDroppedCaptureSamples;
        private bool debugFirstSend = true, debugFirstDrop = true;
        private DateTime debugAudioWindow = DateTime.UtcNow;

        /// <summary>Optional diagnostics. Callbacks are isolated and delivered outside engine locks.</summary>
        public Action<string> DebugLog { get; set; }
        public bool DebugLogEnabled
        {
            get => debugLogEnabled;
            set
            {
                lock (sync)
                {
                    if (debugLogEnabled == value) return;
                    debugLogEnabled = value;
                    ResetDebugAudioLocked();
                    if (value)
                    {
                        debugTransactionId = turns.Values.LastOrDefault()?.Id;
                        TraceLocked($"debug.enabled allow_barge_in={options.AllowBargeIn} suppressed={inputSuppressed} queued_frames={audioQueue.Count} send_in_flight={activeAudio != null}");
                    }
                }
            }
        }

        public event Action<SpeechPipelineResponse> ResponseReceived;
        /// <summary>ResponseReceived with its originating run and generation, safe to retain across controls.
        /// Delivered immediately after ResponseReceived for the same response.</summary>
        public event Action<OrchestratorResponseEvent> ResponseObserved;
        public event Action<AvatarRequest> PresentationStarted;
        internal event Action<OrchestratorPresentationEvent> PresentationDispatched;
        public event Action<AvatarRequest> PresentationCompleted;
        /// <summary>Raised once when a terminal or stopped turn has no remaining presentations.
        /// The response that terminates the turn, when present, is notified first.</summary>
        public event Action<OrchestratorTurnResult> TurnEnded;
        /// <summary>Accepted interrupt/reset boundaries, including when no turn is active.</summary>
        public event Action<OrchestratorLifecycleEvent> LifecycleChanged;
        public event Action<Exception> Error;
        public event Action InputSuppressionChanged;

        /// <summary>Optional application customization before submitting a text/image/audio request.</summary>
        public Func<SpeechPipelineRequest, CancellationToken, UniTask> BeforeRequestAsync { get; set; }
        public string SessionId => pipeline.SessionId;
        /// <summary>The actual presentation target selected for this run, including code injection.</summary>
        public IAvatarController Avatar => avatar;
        /// <summary>Increments when an interrupt/reset is accepted. A new engine begins at zero.</summary>
        public long Generation => Interlocked.Read(ref generation);
        /// <summary>Returns the original start order for an active or retained presentation cutoff.</summary>
        public bool TryGetTurnOrder(string transactionId, out long order)
        {
            lock (sync)
            {
                if (transactionId != null)
                {
                    if (turns.TryGetValue(transactionId, out var turn) && turn.Started)
                    { order = turn.Order; return true; }
                    if (startOrders.TryGetValue(transactionId, out order)) return true;
                }
                order = 0; return false;
            }
        }
        /// <summary>Identifies an active presentation's originating generation and start order.
        /// A terminal turn may still have pending playback; stopped turns and earlier generations are rejected.</summary>
        public bool TryGetActivePresentationContext(string transactionId, out long turnGeneration, out long turnOrder)
        {
            lock (sync)
            {
                if (transactionId != null && turns.TryGetValue(transactionId, out var turn) &&
                    turn.Started && !turn.Stopped && !turn.Ended && turn.Pending > 0 && turn.Generation == generation)
                {
                    turnGeneration = turn.Generation; turnOrder = turn.Order; return true;
                }
                turnGeneration = 0; turnOrder = 0; return false;
            }
        }
        /// <summary>Includes accepted requests, generation, and queued or active playback.</summary>
        public bool HasActiveTurns { get { lock (sync) return turns.Values.Any(turn => !turn.Ended && !turn.Stopped); } }
        public bool IsPresenting { get { lock (sync) return playing?.Request != null; } }
        public bool IsInputSuppressed { get { lock (sync) return inputSuppressed; } }
        // Recognition callbacks can run while a provider holds its lock. Never acquire the core
        // lock from that path: request submission may hold it while calling back into the provider.
        internal bool AdmitsRecognition => Volatile.Read(ref inputEnabled) && !Volatile.Read(ref controlling) &&
            !Volatile.Read(ref disposing) && !Volatile.Read(ref inputFaulted) && !Volatile.Read(ref inputSuppressed);
        /// <summary>Controls forwarding only. Use InterruptAsync to also discard already submitted VAD/recognition work.</summary>
        public bool InputEnabled
        {
            get { lock (sync) return inputEnabled; }
            set { lock (sync) { CheckAlive(); inputEnabled = value; UpdateSuppressionLocked("InputEnabled"); } }
        }
        /// <summary>Changes microphone forwarding during active responses, including pending avatar playback.</summary>
        public bool AllowBargeIn
        {
            get { lock (sync) return options.AllowBargeIn; }
            set
            {
                lock (sync)
                {
                    CheckAlive();
                    if (options.AllowBargeIn == value) return;
                    options.AllowBargeIn = value;
                    if (debugLogEnabled) TraceLocked($"policy.changed allow_barge_in={value}");
                    UpdateSuppressionLocked("AllowBargeIn");
                }
                _ = SetPipelineBargeInAsync(value);
            }
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
            _ = SetPipelineBargeInAsync(this.options.AllowBargeIn);
        }

        public ChatdollOrchestratorOptions GetOptions() { lock (sync) return options.Copy(); }
        public void UpdateOptions(ChatdollOrchestratorOptions replacement)
        {
            var copy = replacement?.Copy() ?? throw new ArgumentNullException(nameof(replacement));
            copy.Validate();
            bool bargeInChanged;
            lock (sync)
            {
                CheckAvailable();
                bargeInChanged = options.AllowBargeIn != copy.AllowBargeIn;
                options = copy;
                if (bargeInChanged && debugLogEnabled) TraceLocked($"policy.changed allow_barge_in={copy.AllowBargeIn} source=UpdateOptions");
                UpdateSuppressionLocked("UpdateOptions");
            }
            if (bargeInChanged) _ = SetPipelineBargeInAsync(copy.AllowBargeIn);
        }

        private async UniTask SetPipelineBargeInAsync(bool allowBargeIn)
        {
            if (debugLogEnabled) lock (sync) TraceLocked($"backend.barge_in.start allow_barge_in={allowBargeIn}");
            try
            {
                await pipeline.SetAllowBargeInAsync(allowBargeIn);
                if (debugLogEnabled) lock (sync) TraceLocked($"backend.barge_in.success allow_barge_in={allowBargeIn}");
            }
            catch (Exception error)
            {
                if (debugLogEnabled) lock (sync) TraceLocked($"backend.barge_in.failure allow_barge_in={allowBargeIn} error_type={error.GetType().Name}");
                ReportError(error);
            }
        }

        /// <summary>Copies one mono PCM16 frame into a bounded queue. False means the frame was not accepted.</summary>
        public bool SubmitAudio(byte[] samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if ((samples.Length & 1) != 0) throw new ArgumentException("PCM16 input must contain complete samples.", nameof(samples));
            lock (sync)
            {
                if (inputSuppressed || disposing)
                {
                    RecordDebugAudioLocked(droppedFrames: 1);
                    return false;
                }
                if (samples.Length == 0) return true;
                if (audioQueue.Count >= options.MaxPendingAudioFrames)
                {
                    inputFaulted = true;
                    RecordDebugAudioLocked(droppedFrames: 1);
                    UpdateSuppressionLocked("QueueOverflow");
                    ReportErrorLocked(new InvalidOperationException("Microphone input queue overflowed. Pending audio was discarded; resume input explicitly after addressing the producer rate."));
                    return false;
                }
                if (audioIdle.Task.Status.IsCompleted()) audioIdle = CompletionSource<bool>();
                audioQueue.Enqueue((byte[])samples.Clone()); audioSignal.Release();
                RecordDebugAudioLocked(queuedFrames: 1);
                return true;
            }
        }

        public void ResumeAudioInput()
        {
            lock (sync) { CheckAvailable(); inputFaulted = false; UpdateSuppressionLocked("ResumeAudioInput"); }
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
            var ended = new List<OrchestratorTurnResult>();
            bool resetSpeechInput;
            lock (sync)
            {
                if (disposing || controlling) return UniTask.CompletedTask;
                if (copy.Type == SpeechPipelineResponseType.Backchannel)
                {
                    // Backchannels are audio-only: no conversation turn, input suppression or normal response notifications.
                    if (copy.AudioData == null || copy.AudioData.Length == 0 ||
                        turns.Values.Any(item => !item.Stopped && (!item.Terminal || item.Pending > 0)) ||
                        playbackQueue.Count(item => item.Request != null) >= options.MaxPendingPresentations)
                        return UniTask.CompletedTask;
                    EnqueuePlaybackLocked(new Playback
                    {
                        BackchannelSource = new CancellationTokenSource(),
                        Request = new AvatarRequest { SessionId = copy.SessionId ?? SessionId, ContextId = copy.ContextId, AudioData = copy.AudioData }
                    });
                    return UniTask.CompletedTask;
                }
                if (copy.Type == SpeechPipelineResponseType.Accepted || copy.Type == SpeechPipelineResponseType.Start ||
                    copy.Type == SpeechPipelineResponseType.Chunk)
                    DiscardQueuedBackchannelsLocked();
                Turn turn = null;
                if (copy.TransactionId != null) turns.TryGetValue(copy.TransactionId, out turn);
                if (turn != null && copy.ContextId != null) turn.ContextId = copy.ContextId;
                if (debugLogEnabled && (copy.Type == SpeechPipelineResponseType.Final || copy.Type == SpeechPipelineResponseType.Canceled ||
                    copy.Type == SpeechPipelineResponseType.Error || copy.Type == SpeechPipelineResponseType.Stop))
                {
                    TraceAudioSummaryLocked(copy.Type.ToString());
                    TraceLocked($"response.{copy.Type} pending_presentations={turn?.Pending ?? 0} queued_frames={audioQueue.Count} send_in_flight={activeAudio != null}", copy.TransactionId);
                }
                switch (copy.Type)
                {
                    case SpeechPipelineResponseType.Accepted:
                        if (string.IsNullOrEmpty(copy.TransactionId) || turn != null) return UniTask.CompletedTask;
                        turn = new Turn
                        {
                            Id = copy.TransactionId, SessionId = copy.SessionId ?? SessionId,
                            ContextId = copy.ContextId, Generation = generation,
                            BlockInput = copy.Metadata?.Value<bool?>("block_barge_in") == true
                        };
                        turns.Add(turn.Id, turn);
                        if (debugLogEnabled)
                        {
                            debugTransactionId = turn.Id;
                            TraceAudioSummaryLocked("Accepted");
                            TraceLocked($"response.Accepted allow_barge_in={options.AllowBargeIn} block_barge_in={turn.BlockInput} queued_frames={audioQueue.Count} send_in_flight={activeAudio != null}", turn.Id);
                        }
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
                            StopTurnsLocked(item => ReferenceEquals(item, turn), cancel, OrchestratorTurnEndReason.Failed, ended);
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
                            StopTurnsLocked(item => ReferenceEquals(item, turn), cancel, OrchestratorTurnEndReason.Failed, ended);
                            ReportErrorLocked(error);
                        }
                        break;
                    case SpeechPipelineResponseType.Stop:
                        long cutoff;
                        if (copy.TransactionId == null) cutoff = nextStartOrder;
                        else if (!startOrders.TryGetValue(copy.TransactionId, out cutoff)) return UniTask.CompletedTask;
                        StopTurnsLocked(item => item.Started && item.Order <= cutoff, cancel, ended: ended);
                        break;
                    case SpeechPipelineResponseType.Canceled:
                    case SpeechPipelineResponseType.Error:
                        if (turn != null)
                        {
                            turn.Terminal = true;
                            StopTurnsLocked(item => ReferenceEquals(item, turn), cancel,
                                copy.Type == SpeechPipelineResponseType.Error ? OrchestratorTurnEndReason.Failed : OrchestratorTurnEndReason.Canceled, ended);
                        }
                        break;
                    case SpeechPipelineResponseType.Final:
                        if (turn != null) { turn.Terminal = true; CleanupTurnLocked(turn, ended); }
                        break;
                    case SpeechPipelineResponseType.ToolCall:
                        if (turn == null || turn.Stopped || turn.Terminal) return UniTask.CompletedTask;
                        break;
                }
                UpdateSuppressionLocked(debugLogEnabled ? copy.Type.ToString() : null, copy.TransactionId);
                resetSpeechInput = copy.Type == SpeechPipelineResponseType.Accepted && inputSuppressed;
                if (debugLogEnabled && copy.Type == SpeechPipelineResponseType.Accepted)
                    TraceLocked($"input.reset_on_accepted requested={resetSpeechInput}", copy.TransactionId);
                var observed = new OrchestratorResponseEvent(this, turn?.Generation ?? generation, copy, turn?.Order ?? 0);
                NotifyLocked(() =>
                {
                    Publish(ResponseReceived, copy, item => item.Copy());
                    Publish(ResponseObserved, observed, item => item.Copy());
                });
                // Even a text-only terminal response must be delivered before its turn end.
                foreach (var result in ended) NotifyTurnEndedLocked(result);
            }
            foreach (var source in cancel) Cancel(source);
            return resetSpeechInput ? pipeline.ResetSpeechInputAsync() : UniTask.CompletedTask;
        }

        private void StopTurnsLocked(Func<Turn, bool> predicate, List<CancellationTokenSource> cancel,
            OrchestratorTurnEndReason reason = OrchestratorTurnEndReason.Canceled, List<OrchestratorTurnResult> ended = null)
        {
            var affected = turns.Values.Where(predicate).ToArray();
            foreach (var turn in affected)
            {
                turn.Stopped = true;
                if (turn.EndReason != OrchestratorTurnEndReason.Failed) turn.EndReason = reason;
                cancel.Add(turn.Source);
            }
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
            foreach (var turn in affected) CleanupTurnLocked(turn, ended);
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

        private void DiscardQueuedBackchannelsLocked()
        {
            for (var node = playbackQueue.First; node != null;)
            {
                var next = node.Next;
                if (node.Value.BackchannelSource != null)
                {
                    node.Value.BackchannelSource.Dispose();
                    playbackQueue.Remove(node);
                }
                node = next;
            }
            if (playing == null && playbackQueue.Count == 0) playbackIdle.TrySetResult(true);
        }

        private void StopBackchannelsLocked(List<CancellationTokenSource> cancel)
        {
            DiscardQueuedBackchannelsLocked();
            if (playing?.BackchannelSource != null) cancel.Add(playing.BackchannelSource);
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
                        // Dispatch is the backchannel start boundary. Preparation inside an avatar may still follow.
                        var token = (item.Turn?.Source ?? item.BackchannelSource).Token;
                        token.ThrowIfCancellationRequested();
                        if (item.Turn != null) lock (sync)
                        {
                            var dispatched = new OrchestratorPresentationEvent(this, item.Turn.Generation, item.Turn.Order, item.Request);
                            NotifyLocked(() =>
                            {
                                Publish(PresentationStarted, item.Request, request => request.Copy());
                                Publish(PresentationDispatched, dispatched, value => value.Copy());
                            });
                        }
                        await avatar.PresentAsync(item.Request.Copy(), token);
                        token.ThrowIfCancellationRequested();
                        if (item.Turn != null) lock (sync) NotifyLocked(() => Publish(PresentationCompleted, item.Request, request => request.Copy()));
                    }
                }
                catch (OperationCanceledException) when (item.Turn?.Source.IsCancellationRequested == true || item.BackchannelSource?.IsCancellationRequested == true) { }
                catch (Exception error)
                {
                    item.StopCompletion?.TrySetException(error);
                    ReportError(error);
                    if (item.Turn != null)
                    {
                        var cancel = new List<CancellationTokenSource>();
                        lock (sync) StopTurnsLocked(turn => ReferenceEquals(turn, item.Turn), cancel, OrchestratorTurnEndReason.Failed);
                        foreach (var source in cancel) Cancel(source);
                    }
                }
                finally
                {
                    lock (sync)
                    {
                        playing = null;
                        item.BackchannelSource?.Dispose();
                        if (item.Turn != null) { item.Turn.Pending--; CleanupTurnLocked(item.Turn); }
                        if (playbackQueue.Count == 0) playbackIdle.TrySetResult(true);
                        UpdateSuppressionLocked("PresentationCompleted", item.Turn?.Id);
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
                    RecordDebugAudioLocked(sentFrames: 1);
                }
                try { await pipeline.ProcessAudioSamplesAsync(frame, source.Token); }
                catch (OperationCanceledException) when (source.IsCancellationRequested) { }
                catch (Exception error)
                {
                    lock (sync)
                    {
                        inputFaulted = true;
                        if (debugLogEnabled) TraceLocked($"audio.send.failure error_type={error.GetType().Name}");
                        UpdateSuppressionLocked("AudioSendFailure"); ReportErrorLocked(error);
                    }
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
                var boundary = new OrchestratorLifecycleEvent(this,
                    reset ? OrchestratorLifecycleKind.Reset : OrchestratorLifecycleKind.Interrupted,
                    SessionId, Interlocked.Increment(ref generation), contextId);
                NotifyLocked(() => Publish(LifecycleChanged, boundary, value => value));
                completion = CompletionSource<bool>(); controlTask = completion.Task;
                cancel = invocations.Select(item => item.Source).ToList();
                if (activeAudio != null) cancel.Add(activeAudio);
                StopBackchannelsLocked(cancel);
                StopTurnsLocked(item => true, cancel);
                stop = EnqueueStopBarrierLocked();
                if (debugLogEnabled) TraceLocked($"control.started kind={(reset ? "Reset" : "Interrupt")}");
                UpdateSuppressionLocked(reset ? "Reset" : "Interrupt");
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
                    if (debugLogEnabled) TraceLocked($"control.completed kind={(reset ? "Reset" : "Interrupt")} success={failure == null}");
                    UpdateSuppressionLocked("ControlCompleted");
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
                StopBackchannelsLocked(cancel);
                StopTurnsLocked(item => true, cancel);
                EnqueueStopBarrierLocked();
                if (debugLogEnabled) { TraceAudioSummaryLocked("Dispose"); TraceLocked("engine.disposing"); }
                UpdateSuppressionLocked("Dispose"); audioSignal.Release(); playbackSignal.Release();
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

        private void CleanupTurnLocked(Turn turn, List<OrchestratorTurnResult> ended = null)
        {
            if (!turn.Ended && turn.Pending == 0 && (turn.Terminal || turn.Stopped))
            {
                turn.Ended = true;
                if (debugLogEnabled) TraceLocked($"turn.ended reason={turn.EndReason}", turn.Id);
                turns.Remove(turn.Id); turn.Source.Dispose();
                var result = new OrchestratorTurnResult(this, turn.SessionId, turn.ContextId, turn.Id, turn.Generation, turn.EndReason);
                if (ended != null) ended.Add(result); else NotifyTurnEndedLocked(result);
            }
            // Retain stopped-generation cutoffs while any earlier audio still exists, including after Final.
            var pending = turns.Values.Where(item => item.Started && item.Pending > 0).Select(item => item.Order);
            var minimum = pending.DefaultIfEmpty(nextStartOrder + 1).Min();
            foreach (var id in startOrders.Where(item => item.Value < minimum && item.Key != lastStartedId).Select(item => item.Key).ToArray())
                startOrders.Remove(id);
        }

        private void NotifyTurnEndedLocked(OrchestratorTurnResult result) =>
            NotifyLocked(() => Publish(TurnEnded, result, value => value));

        private void UpdateSuppressionLocked(string cause, string transactionId = null)
        {
            var suppressed = !inputEnabled || inputFaulted || controlling || disposing ||
                turns.Values.Any(turn => !turn.Stopped && (!turn.Terminal || turn.Pending > 0) &&
                    (turn.BlockInput || !options.AllowBargeIn));
            if (suppressed == inputSuppressed) return;
            if (debugLogEnabled)
            {
                TraceAudioSummaryLocked("input_boundary");
                var blocked = turns.Values.Any(turn => !turn.Stopped && (!turn.Terminal || turn.Pending > 0) && turn.BlockInput);
                TraceLocked($"input.{(suppressed ? "suppressed" : "resumed")} cause={cause} input_enabled={inputEnabled} faulted={inputFaulted} controlling={controlling} disposing={disposing} allow_barge_in={options.AllowBargeIn} block_barge_in={blocked} cleared_queued_frames={(suppressed ? audioQueue.Count : 0)} send_in_flight={activeAudio != null}", transactionId);
                ResetDebugAudioLocked();
            }
            inputSuppressed = suppressed;
            if (suppressed)
            {
                audioQueue.Clear();
                if (activeAudio == null) audioIdle.TrySetResult(true);
            }
            NotifyLocked(() => Publish(InputSuppressionChanged));
        }

        // Microphone conversion discards suppressed capture before SubmitAudio is called.
        // Record only its size; never retain or print the samples.
        internal void RecordMicrophoneInput(int sampleCount, bool suppressed)
        {
            if (!debugLogEnabled || !suppressed) return;
            lock (sync) RecordDebugAudioLocked(droppedCaptureSamples: sampleCount);
        }

        private void RecordDebugAudioLocked(int queuedFrames = 0, int sentFrames = 0,
            int droppedFrames = 0, int droppedCaptureSamples = 0)
        {
            if (!debugLogEnabled) return;
            debugQueuedFrames += queuedFrames; debugSentFrames += sentFrames;
            debugDroppedFrames += droppedFrames; debugDroppedCaptureSamples += droppedCaptureSamples;
            if (sentFrames > 0 && debugFirstSend)
            {
                debugFirstSend = false;
                TraceLocked($"audio.send.first queued_frames={audioQueue.Count} suppressed={inputSuppressed}");
            }
            if ((droppedFrames > 0 || droppedCaptureSamples > 0) && debugFirstDrop)
            {
                debugFirstDrop = false;
                TraceLocked($"audio.drop.first source={(droppedCaptureSamples > 0 ? "microphone_converter" : "SubmitAudio")} suppressed={inputSuppressed}");
            }
            if ((DateTime.UtcNow - debugAudioWindow).TotalSeconds >= 1) TraceAudioSummaryLocked("interval");
        }

        private void TraceAudioSummaryLocked(string reason)
        {
            if (debugQueuedFrames + debugSentFrames + debugDroppedFrames + debugDroppedCaptureSamples == 0) return;
            TraceLocked($"audio.summary reason={reason} queued_frames={debugQueuedFrames} send_started_frames={debugSentFrames} rejected_frames={debugDroppedFrames} dropped_capture_samples={debugDroppedCaptureSamples} pending_frames={audioQueue.Count} send_in_flight={activeAudio != null}");
            debugQueuedFrames = debugSentFrames = debugDroppedFrames = debugDroppedCaptureSamples = 0;
            debugAudioWindow = DateTime.UtcNow;
        }

        private void ResetDebugAudioLocked()
        {
            debugQueuedFrames = debugSentFrames = debugDroppedFrames = debugDroppedCaptureSamples = 0;
            debugFirstSend = debugFirstDrop = true;
            debugAudioWindow = DateTime.UtcNow;
        }

        private void TraceLocked(string message, string transactionId = null)
        {
            var logger = DebugLog;
            if (!debugLogEnabled || logger == null) return;
            var entry = $"[ChatdollOrchestrator] utc={DateTime.UtcNow:O} session={DebugId(SessionId)} transaction={DebugId(transactionId ?? debugTransactionId)} {message}";
            NotifyLocked(() => { if (debugLogEnabled) try { logger(entry); } catch { } });
        }

        internal static string DebugId(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            var bounded = value.Length > 128 ? value.Substring(0, 128) : value;
            return new string(bounded.Select(character => char.IsControl(character) ? '_' : character).ToArray());
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

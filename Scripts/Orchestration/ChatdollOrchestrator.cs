using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechListener;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.Performance;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.VAD;
using UnityEngine;

namespace ChatdollKit.Orchestration
{
    /// <summary>Unity entry point for one conversation. Configure dependencies before StartAsync.
    /// Lifecycle methods run on Unity's main thread; engine notifications are delivered from Update.
    /// Run start/stop boundaries are delivered immediately, including when disabled.</summary>
    [DisallowMultipleComponent, AddComponentMenu("ChatdollKit/Orchestration/Chatdoll Orchestrator")]
    public sealed partial class ChatdollOrchestrator : MonoBehaviour
    {
        public bool AutoStart;
        [Tooltip("Inspector configuration for the pipeline. A single component on this GameObject is selected automatically. Code injection takes precedence.")]
        public SpeechPipelineComponent PipelineComponent;
        public ChatdollKit.Avatar.AvatarController Avatar;
        [Tooltip("Optional for text-only operation. Do not attach another VAD adapter to the same input path.")]
        public MicrophoneManager Microphone;
        public ChatdollOrchestratorOptions Options = new ChatdollOrchestratorOptions();
        public Func<SpeechPipelineRequest, CancellationToken, UniTask> BeforeRequestAsync { get; set; }

        public bool IsRunning => active != null && active.Accepting;
        public ChatdollOrchestratorEngine Orchestrator => active?.Core;
        public ISpeechPipeline Pipeline => active?.Pipeline;
        /// <summary>Microphone interruption policy. Changes apply immediately without restarting the conversation.</summary>
        public bool AllowBargeIn
        {
            get => Options?.AllowBargeIn ?? throw new ArgumentNullException(nameof(Options));
            set => SetAllowBargeIn(value);
        }
        public bool InputEnabled
        {
            get => inputEnabled;
            set
            {
                EnsureMainThread();
                inputEnabled = value;
                if (active != null) { active.Input?.Reset(); active.Core.InputEnabled = value; }
            }
        }
        public event Action<SpeechPipelineResponse> ResponseReceived;
        public event Action<OrchestratorResponseEvent> ResponseObserved;
        public event Action<AvatarRequest> PresentationStarted;
        public event Action<AvatarRequest> PresentationCompleted;
        public event Action<OrchestratorTurnResult> TurnEnded;
        public event Action<OrchestratorLifecycleEvent> LifecycleChanged;
        public event Action<Exception> Error;

        private sealed class Run
        {
            internal ChatdollOrchestratorEngine Core;
            internal ISpeechPipeline Pipeline;
            internal SpeechPipelineLease Lease;
            internal MicrophoneManager Microphone;
            internal volatile OrchestratorMicrophoneInput Input;
            internal int InputSampleRate, SamplesPerMessage;
            internal bool Accepting = true;
            internal bool AppliedAllowBargeIn;
            internal bool LifecycleStarted, LifecycleStopped;
            internal Action<float[]> SamplesHandler;
            internal Action<SpeechPipelineResponse> ResponseHandler;
            internal Action<OrchestratorResponseEvent> ObservedHandler;
            internal Action<OrchestratorPresentationEvent> DispatchedHandler;
            internal Action<AvatarRequest> StartedHandler, CompletedHandler;
            internal Action<OrchestratorTurnResult> TurnEndedHandler;
            internal Action<OrchestratorLifecycleEvent> LifecycleHandler;
            internal Action SuppressionHandler;
            internal AvatarController MessageAvatar;
            internal Action<AvatarRequest> MessageStartedHandler;
            internal ISpeechRecognitionSource RecognitionSource;
            internal Action<SpeechRecognitionUpdate> RecognitionHandler;
            internal long MessageGeneration;
            internal readonly string ConversationRunId = Guid.NewGuid().ToString("N");
            internal Action<Exception> ErrorHandler;
        }

        private sealed class PendingHook
        {
            internal int State; // 0: queued, 1: executing, 2: canceled before execution.
            internal readonly SpeechCompletionSource<bool> Completion = new SpeechCompletionSource<bool>();
        }

        private readonly SpeechAsyncSemaphore lifecycle = new SpeechAsyncSemaphore(1, 1);
        private readonly SpeechCallbackGuard lifecycleCallbacks = new SpeechCallbackGuard();
        private readonly ConcurrentQueue<Action> notifications = new ConcurrentQueue<Action>();
        private readonly Queue<OrchestratorLifecycleEvent> lifecycleNotifications = new Queue<OrchestratorLifecycleEvent>();
        private bool publishingLifecycle;
        private Run active;
        private IAvatarController avatarOverride;
        private ISpeechPipeline configuredPipeline;
        private Func<CancellationToken, UniTask<ISpeechPipeline>> pipelineFactory;
        private int configuredInputSampleRate = 16000, configuredSamplesPerMessage = 512;
        private bool ownsConfiguredPipeline, ownsFactoryPipeline = true;
        private bool inputEnabled = true, unityStarted, destroyed;
        private CancellationTokenSource starting;
        private UniTask pendingStart;
        private int version, pendingStartVersion, mainThreadId;

        private void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            ResolveComponents();
        }
        /// <summary>Fill missing same-object references. Safe to call from Main's Awake regardless of component Awake order.</summary>
        public void ResolveComponents()
        {
            if (Avatar == null) Avatar = GetComponent<ChatdollKit.Avatar.AvatarController>();
            if (Microphone == null) Microphone = GetComponent<MicrophoneManager>();
            InitializeMicrophoneProvider();
        }
        private void Start() { unityStarted = true; if (AutoStart) _ = ObserveAsync(StartAsync); }
        private void OnEnable() { if (unityStarted && AutoStart) _ = ObserveAsync(StartAsync); }
        private void OnDisable() { if (mainThreadId != 0) _ = ObserveAsync(BeginStopAsync); }
        private void OnDestroy() { destroyed = true; if (mainThreadId != 0) _ = ObserveAsync(BeginStopAsync); }
        private void Update()
        {
            // Read Inspector changes on the main thread, without running core logic from OnValidate.
            ApplyAllowBargeIn();
            RefreshConversationState();
            while (notifications.TryDequeue(out var notification))
                try { notification(); } catch (Exception error) { Report(error); }
            RefreshConversationState();
        }

        /// <summary>Bind a UI Toggle's dynamic bool event here. May also configure the policy before startup.</summary>
        public void SetAllowBargeIn(bool allowBargeIn)
        {
            // Main's Awake may run before this component's Awake. Before initialization this only changes settings.
            if (mainThreadId != 0) EnsureMainThread();
            if (destroyed) throw new ObjectDisposedException(nameof(ChatdollOrchestrator));
            var settings = Options ?? throw new ArgumentNullException(nameof(Options));
            settings.AllowBargeIn = allowBargeIn;
            ApplyAllowBargeIn(force: true);
        }

        private void ApplyAllowBargeIn(bool force = false)
        {
            var run = active;
            if (run == null || !run.Accepting || Options == null) return;
            var allowBargeIn = Options.AllowBargeIn;
            if (!force && run.AppliedAllowBargeIn == allowBargeIn) return;
            if (run.Core.AllowBargeIn != allowBargeIn)
            {
                run.Core.AllowBargeIn = allowBargeIn;
                // Discard a buffered fragment even if the UI toggles off and on between microphone callbacks.
                run.Input?.RequestReset();
            }
            run.AppliedAllowBargeIn = allowBargeIn;
        }

        /// <summary>Transfers ownership only when StartAsync acquires the pipeline. An owned instance
        /// is consumed once; configure a factory to create a fresh instance on every start.
        /// Supply the injected pipeline's mono PCM16 input format here.</summary>
        public void ConfigurePipeline(ISpeechPipeline pipeline, bool ownsPipeline = false,
            int inputSampleRate = 16000, int samplesPerMessage = 512)
        {
            EnsureConfigurable();
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ValidateInputFormat(inputSampleRate, samplesPerMessage);
            configuredPipeline = pipeline;
            ownsConfiguredPipeline = ownsPipeline;
            configuredInputSampleRate = inputSampleRate;
            configuredSamplesPerMessage = samplesPerMessage;
            pipelineFactory = null;
        }

        /// <summary>Creates a pipeline on each start, using the supplied mono PCM16 input format.</summary>
        public void ConfigurePipelineFactory(Func<CancellationToken, UniTask<ISpeechPipeline>> factory, bool ownsPipeline = true,
            int inputSampleRate = 16000, int samplesPerMessage = 512)
        {
            EnsureConfigurable();
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            ValidateInputFormat(inputSampleRate, samplesPerMessage);
            pipelineFactory = factory;
            ownsFactoryPipeline = ownsPipeline;
            configuredInputSampleRate = inputSampleRate;
            configuredSamplesPerMessage = samplesPerMessage;
            configuredPipeline = null;
        }

        private static void ValidateInputFormat(int inputSampleRate, int samplesPerMessage)
        {
            if (inputSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputSampleRate));
            if (samplesPerMessage <= 0) throw new ArgumentOutOfRangeException(nameof(samplesPerMessage));
        }

        public void ConfigureAvatar(IAvatarController avatar)
        {
            EnsureConfigurable();
            avatarOverride = avatar ?? throw new ArgumentNullException(nameof(avatar));
        }

        /// <summary>Creates a local pipeline on each start. Supplied providers, VAD, and recorder
        /// remain caller-owned, so they can be reused after stopping this orchestrator.</summary>
        public void ConfigureLocalPipeline(ISpeechRecognizer stt, ILlmService llm, ISpeechSynthesizer tts,
            SpeechPipelineOptions pipelineOptions = null, ISpeechDetector vad = null,
            IPipelinePerformanceRecorder performanceRecorder = null, ISpeechPipelineClock clock = null,
            LlmHistoryFormat? historyFormat = null)
        {
            EnsureConfigurable();
            if (stt == null) throw new ArgumentNullException(nameof(stt));
            if (llm == null) throw new ArgumentNullException(nameof(llm));
            if (tts == null) throw new ArgumentNullException(nameof(tts));
            var snapshot = (pipelineOptions ?? new SpeechPipelineOptions()).Copy();
            snapshot.Validate();
            var sttSampleRate = (stt as SpeechRecognizerBase)?.GetOptions().SampleRate;
            var inputSampleRate = vad?.SampleRate ?? sttSampleRate ?? 16000;
            if (vad != null && (vad.Channels != 1 || (sttSampleRate.HasValue && vad.SampleRate != sttSampleRate.Value)))
                throw new ArgumentException("VAD and STT must use the same mono PCM sample rate.");
            var samplesPerMessage = (vad?.GetOptions() as SileroSpeechDetectorOptions)?.ChunkSize
                ?? Math.Max(1, inputSampleRate / 50);
            ConfigurePipelineFactory(token =>
            {
                token.ThrowIfCancellationRequested();
                if ((vad != null && (vad.Channels != 1 || vad.SampleRate != inputSampleRate)) ||
                    (stt is SpeechRecognizerBase recognizer && recognizer.GetOptions().SampleRate != inputSampleRate))
                    throw new ArgumentException("The provider audio format changed. ConfigureLocalPipeline again before starting.");
                return UniTask.FromResult<ISpeechPipeline>(new SpeechToSpeechPipeline(stt, llm, tts,
                    snapshot, vad, performanceRecorder, clock, ownsComponents: false, historyFormat: historyFormat));
            }, inputSampleRate: inputSampleRate, samplesPerMessage: samplesPerMessage);
        }

        public UniTask StartAsync()
        {
            EnsureMainThread(); RejectLifecycleReentry();
            if (destroyed) throw new ObjectDisposedException(nameof(ChatdollOrchestrator));
            if (IsRunning) return UniTask.CompletedTask;
            if (!pendingStart.Status.IsCompleted() && pendingStartVersion == version) return pendingStart;
            InitializeMicrophoneProvider();
            var requestVersion = ++version;
            Cancel(starting);
            var startup = starting = new CancellationTokenSource();
            pendingStartVersion = requestVersion;
            pendingStart = SpeechAsync.Share(StartCoreAsync(requestVersion, startup));
            return pendingStart;
        }

        private async UniTask StartCoreAsync(int requestVersion, CancellationTokenSource startup)
        {
            ISpeechPipeline pending = null;
            SpeechPipelineLease pendingLease = null;
            var ownsPending = false;
            var acquired = false;
            try
            {
                // Let every component's Start run, including MicrophoneManager's WebGL initialization.
                await SpeechAsync.Yield();
                await lifecycle.WaitAsync(startup.Token); acquired = true;
                startup.Token.ThrowIfCancellationRequested();
                await StopRunCoreAsync();
                if (requestVersion != version || !isActiveAndEnabled) throw new OperationCanceledException(startup.Token);
                var settings = Options?.Copy() ?? throw new ArgumentNullException(nameof(Options));
                settings.Validate();
                var inputSampleRate = configuredInputSampleRate;
                var samplesPerMessage = configuredSamplesPerMessage;
                var avatar = avatarOverride ?? (IAvatarController)Avatar;
                if (avatar == null) throw new InvalidOperationException("Configure an IAvatarController before starting.");
                var microphone = Microphone;
                if (configuredPipeline != null)
                {
                    pending = configuredPipeline; ownsPending = ownsConfiguredPipeline;
                    if (ownsPending) configuredPipeline = null;
                }
                else if (pipelineFactory != null)
                {
                    ownsPending = ownsFactoryPipeline;
                    pending = await lifecycleCallbacks.InvokeAsync(() => pipelineFactory(startup.Token));
                }
                else
                {
                    var component = ResolvePipelineComponent();
                    pendingLease = await lifecycleCallbacks.InvokeAsync(() => component.CreatePipelineAsync(startup.Token));
                    if (pendingLease == null) throw new InvalidOperationException("The pipeline component returned no run.");
                    pending = pendingLease.Pipeline;
                    inputSampleRate = pendingLease.InputSampleRate;
                    samplesPerMessage = pendingLease.SamplesPerMessage;
                }
                startup.Token.ThrowIfCancellationRequested();
                if (requestVersion != version) throw new OperationCanceledException(startup.Token);
                if (pending == null) throw new InvalidOperationException("The pipeline factory returned null.");
                // Provider creation can yield while the UI changes this live setting.
                settings.AllowBargeIn = AllowBargeIn;
                var core = new ChatdollOrchestratorEngine(pending, avatar, settings, ownsPending, lifecycleCallbacks);
                var run = new Run
                {
                    Core = core, Pipeline = pending, Lease = pendingLease, Microphone = microphone,
                    InputSampleRate = inputSampleRate, SamplesPerMessage = samplesPerMessage,
                    AppliedAllowBargeIn = settings.AllowBargeIn
                };
                pending = null; pendingLease = null;
                active = run;
                try
                {
                    var beforeRequest = BeforeRequestAsync;
                    if (beforeRequest != null)
                        core.BeforeRequestAsync = (request, token) => DispatchHookAsync(run, beforeRequest, request, token);
                    Attach(run);
                    core.InputEnabled = inputEnabled;
                    run.LifecycleStarted = true;
                    PublishLifecycle(new OrchestratorLifecycleEvent(core,
                        OrchestratorLifecycleKind.Started, core.SessionId, core.Generation));
                }
                catch { await StopRunCoreAsync(); throw; }
            }
            finally
            {
                try
                {
                    if (pendingLease != null) await pendingLease.DisposeAsync();
                    else if (pending != null && ownsPending) await pending.DisposeAsync();
                }
                finally
                {
                    if (ReferenceEquals(starting, startup)) starting = null;
                    startup.Dispose();
                    if (acquired) lifecycle.Release();
                }
            }
        }

        public UniTask StopAsync()
        {
            EnsureMainThread(); RejectLifecycleReentry();
            return BeginStopAsync();
        }

        public async UniTask RestartAsync()
        {
            await StopAsync();
            if (this != null && isActiveAndEnabled) await StartAsync();
        }

        [ContextMenu("Start Pipeline")]
        private void StartPipeline() => _ = ObserveAsync(StartAsync);
        [ContextMenu("Stop Pipeline")]
        private void StopPipeline() => _ = ObserveAsync(StopAsync);
        [ContextMenu("Restart Pipeline")]
        private void RestartPipeline() => _ = ObserveAsync(RestartAsync);

        private SpeechPipelineComponent ResolvePipelineComponent()
        {
            if (PipelineComponent != null)
            {
                if (!PipelineComponent.isActiveAndEnabled) throw new InvalidOperationException("Enable the selected pipeline component.");
                return PipelineComponent;
            }
            var candidates = GetComponents<SpeechPipelineComponent>().Where(component => component.isActiveAndEnabled).ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException("Assign a SpeechPipelineComponent in the Inspector, or configure a pipeline from code.");
            return candidates[0];
        }

        // Unity may disable/destroy this component from inside its request hook.
        // Starting cleanup without awaiting it in that hook does not form a reentry cycle.
        private UniTask BeginStopAsync()
        {
            ++version; Cancel(starting);
            if (active != null) { DetachInput(active); NotifyRunStopped(active); }
            return StopCoreAsync();
        }

        private async UniTask StopCoreAsync()
        {
            await lifecycle.WaitAsync();
            try { await StopRunCoreAsync(); }
            finally { lifecycle.Release(); }
        }

        private void Attach(Run run)
        {
            run.ResponseHandler = response => Post(run, () => Publish(ResponseReceived, response, value => value.Copy()));
            run.ObservedHandler = observed => Post(run, () =>
            {
                UpdateResponseConversation(run, observed);
                Publish(ResponseObserved, observed, value => value.Copy());
            });
            run.StartedHandler = request => Post(run, () => Publish(PresentationStarted, request, value => value.Copy()));
            run.DispatchedHandler = dispatched => Post(run, () =>
            {
                if (run.MessageAvatar != null || !IsCurrentConversationRun(run) || dispatched.Generation != run.Core.Generation) return;
                RefreshConversationState();
                ChangeConversation(() => conversation.OnPresentationStarted(dispatched.Request, dispatched.TurnOrder, true));
            });
            run.CompletedHandler = request => Post(run, () => Publish(PresentationCompleted, request, value => value.Copy()));
            run.TurnEndedHandler = result => Post(run, () =>
            {
                UpdateEndedConversation(run, result);
                Publish(TurnEnded, result, value => value);
            });
            run.LifecycleHandler = boundary => Post(run, () =>
            {
                RefreshConversationState();
                PublishLifecycle(boundary);
            });
            run.ErrorHandler = error => Post(run, () =>
            {
                ChangeConversation(() => conversation.OnError(error));
                Report(error);
            });
            run.SuppressionHandler = () => run.Input?.RequestReset();
            run.Core.ResponseReceived += run.ResponseHandler;
            run.Core.ResponseObserved += run.ObservedHandler;
            run.Core.PresentationStarted += run.StartedHandler;
            run.Core.PresentationDispatched += run.DispatchedHandler;
            run.Core.PresentationCompleted += run.CompletedHandler;
            run.Core.TurnEnded += run.TurnEndedHandler;
            run.Core.LifecycleChanged += run.LifecycleHandler;
            run.Core.Error += run.ErrorHandler;
            run.Core.InputSuppressionChanged += run.SuppressionHandler;
            AttachConversationSource(run);
            if (run.Microphone != null)
            {
                run.SamplesHandler = samples => ReceiveSamples(run, samples);
                run.Microphone.OnSamplesReceived += run.SamplesHandler;
            }
        }

        private void ReceiveSamples(Run run, float[] samples)
        {
            if (!run.Accepting) return;
            try
            {
                // Capture can start after the pipeline. Its actual channel count is available with the first samples.
                if (run.Input == null)
                    run.Input = new OrchestratorMicrophoneInput(run.Microphone.SampleRate, run.Microphone.Channels,
                        run.InputSampleRate, run.SamplesPerMessage);
                if (run.Microphone.Channels != run.Input.InputChannels)
                    throw new InvalidOperationException("Microphone channel count changed. Stop and restart the orchestrator.");
                foreach (var frame in run.Input.Convert(samples, run.Microphone.SampleRate, run.Core.IsInputSuppressed))
                    if (!run.Core.SubmitAudio(frame)) { run.Input.Reset(); break; }
            }
            catch (Exception error) { Report(error); _ = ObserveAsync(StopAsync); }
        }

        private void DetachInput(Run run)
        {
            run.Accepting = false;
            DetachConversationSource(run);
            if (run.Microphone != null) run.Microphone.OnSamplesReceived -= run.SamplesHandler;
            run.Input?.Reset();
        }

        private async UniTask StopRunCoreAsync()
        {
            var run = active; active = null;
            if (run == null) return;
            DetachInput(run);
            NotifyRunStopped(run);
            var errors = new List<Exception>();
            try
            {
                // Cancel queued live updates before interrupting the conversation they may be waiting on.
                if (run.Lease != null)
                    try { _ = run.Lease.StopSettingsUpdatesAsync(); } catch (Exception error) { errors.Add(error); }
                // Engine-driven disable may run inside a request hook. The core's independent
                // shutdown must not inherit that hook's reentry guard.
                try { await DisposeCoreDetachedAsync(run.Core); } catch (Exception error) { errors.Add(error); }
                if (run.Lease != null)
                    try { await run.Lease.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            }
            finally
            {
                run.Core.ResponseReceived -= run.ResponseHandler;
                run.Core.ResponseObserved -= run.ObservedHandler;
                run.Core.PresentationStarted -= run.StartedHandler;
                run.Core.PresentationDispatched -= run.DispatchedHandler;
                run.Core.PresentationCompleted -= run.CompletedHandler;
                run.Core.TurnEnded -= run.TurnEndedHandler;
                run.Core.LifecycleChanged -= run.LifecycleHandler;
                run.Core.Error -= run.ErrorHandler;
                run.Core.InputSuppressionChanged -= run.SuppressionHandler;
                while (notifications.TryDequeue(out _)) { }
            }
            if (errors.Count > 0) throw new AggregateException(errors);
        }

        private void NotifyRunStopped(Run run)
        {
            if (!run.LifecycleStarted || run.LifecycleStopped) return;
            run.LifecycleStopped = true;
            // Update will no longer run after OnDisable. Deliver this boundary on the lifecycle caller's
            // main thread, before old run notifications are discarded by shutdown.
            PublishLifecycle(new OrchestratorLifecycleEvent(run.Core,
                OrchestratorLifecycleKind.Stopped, run.Core.SessionId, run.Core.Generation));
        }

        private void PublishLifecycle(OrchestratorLifecycleEvent boundary)
        {
            lifecycleNotifications.Enqueue(boundary);
            if (publishingLifecycle) return;
            publishingLifecycle = true;
            try
            {
                // A Started subscriber may synchronously stop/disable the run. Finish notifying Started
                // to every subscriber before delivering that nested Stopped boundary on this same call stack.
                while (lifecycleNotifications.Count > 0)
                {
                    var next = lifecycleNotifications.Dequeue();
                    UpdateConversationLifecycle(next);
                    Publish(LifecycleChanged, next, value => value);
                }
            }
            finally { publishingLifecycle = false; }
        }

        public UniTask<SpeechPipelineResponse> SendTextAsync(string text, CancellationToken cancellationToken = default) =>
            GetRunning().SendTextAsync(text, cancellationToken);
        public UniTask<SpeechPipelineResponse> InvokeAsync(SpeechPipelineRequest request, CancellationToken cancellationToken = default) =>
            GetRunning().InvokeAsync(request, cancellationToken);
        public UniTask InterruptAsync(CancellationToken cancellationToken = default)
        {
            var operation = GetRunning().InterruptAsync(cancellationToken);
            RefreshConversationState();
            return operation;
        }
        public UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default)
        {
            var operation = GetRunning().ResetAsync(contextId, cancellationToken);
            RefreshConversationState();
            return operation;
        }
        public UniTask DrainAsync() => GetRunning().DrainAsync();

        private ChatdollOrchestratorEngine GetRunning()
        {
            EnsureMainThread(); RejectLifecycleReentry();
            return IsRunning ? active.Core : throw new InvalidOperationException("Start the orchestrator first.");
        }
        private async UniTask DispatchHookAsync(Run run, Func<SpeechPipelineRequest, CancellationToken, UniTask> hook,
            SpeechPipelineRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var pending = new PendingHook();
            using (token.Register(() =>
            {
                if (Interlocked.CompareExchange(ref pending.State, 2, 0) == 0) pending.Completion.TrySetCanceled();
            }))
            {
                notifications.Enqueue(() =>
                {
                    if (Interlocked.CompareExchange(ref pending.State, 1, 0) != 0) return;
                    if (!ReferenceEquals(active, run) || !run.Accepting) { pending.Completion.TrySetCanceled(); return; }
                    try { _ = RunHookAsync(run, hook, request, token, pending); }
                    catch (Exception error) { pending.Completion.TrySetException(error); }
                });
                await pending.Completion.Task;
            }
        }
        private async UniTask RunHookAsync(Run run, Func<SpeechPipelineRequest, CancellationToken, UniTask> hook,
            SpeechPipelineRequest request, CancellationToken token, PendingHook pending)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await lifecycleCallbacks.InvokeAsync(() => hook(request, token));
                token.ThrowIfCancellationRequested();
                if (!run.Accepting || !ReferenceEquals(active, run)) throw new OperationCanceledException(token);
                pending.Completion.TrySetResult(true);
            }
            catch (OperationCanceledException) { pending.Completion.TrySetCanceled(); }
            catch (Exception error) { pending.Completion.TrySetException(error); }
        }
        private UniTask DisposeCoreDetachedAsync(ChatdollOrchestratorEngine core)
            => SpeechAsync.Run(core.DisposeAsync);
        private void Post(Run run, Action notification) => notifications.Enqueue(() => { if (ReferenceEquals(active, run)) notification(); });
        private void Publish<T>(Action<T> handlers, T value, Func<T, T> copy)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(copy(value)); } catch (Exception error) { Report(error); }
        }
        private void Cancel(CancellationTokenSource cancellation)
        { try { cancellation?.Cancel(); } catch (ObjectDisposedException) { } catch (Exception error) { Report(error); } }
        private void Report(Exception error)
        {
            var handlers = Error;
            if (handlers == null) { Debug.LogException(error); return; }
            foreach (Action<Exception> handler in handlers.GetInvocationList())
                try { handler(error); } catch (Exception observerError) { Debug.LogException(observerError); }
        }
        private async UniTask ObserveAsync(Func<UniTask> operation)
        { try { await operation(); } catch (OperationCanceledException) { } catch (Exception error) { Report(error); } }
        private void EnsureConfigurable()
        {
            EnsureMainThread(); RejectLifecycleReentry();
            if (destroyed) throw new ObjectDisposedException(nameof(ChatdollOrchestrator));
            if (active != null || starting != null || lifecycle.CurrentCount == 0)
                throw new InvalidOperationException("Stop the orchestrator before replacing its dependencies.");
        }
        private void RejectLifecycleReentry()
        { lifecycleCallbacks.ThrowIfActive("Schedule orchestrator operations after its request hook, pipeline factory, or disposal callback returns."); }
        private void EnsureMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
                throw new InvalidOperationException("Call the orchestrator after Awake on Unity's main thread.");
        }
        private void InitializeMicrophoneProvider()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            // AutoStart=false leaves this unset in the existing manager, but its Update reads it.
            // Preparing a provider does not start capture and preserves application-supplied providers.
            if (Microphone != null && Microphone.MicrophoneProvider == null)
                Microphone.MicrophoneProvider = new UnityMicrophoneProvider();
#endif
        }
    }
}

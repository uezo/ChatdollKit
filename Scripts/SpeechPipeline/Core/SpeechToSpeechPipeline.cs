using ChatdollKit.SpeechPipeline.Performance;
// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.VAD;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline
{
    /// <summary>Single-session orchestration. Playback, microphone muting, and wire protocols belong to the frontend.</summary>
    public sealed class SpeechToSpeechPipeline : ISpeechPipeline, ISpeechRecognitionSource
    {
        private static readonly Regex LanguagePattern = new Regex(@"\[(?:lang|language):([a-zA-Z-]+)\]|<(?:lang|language)\s[^>]*code=[""']([a-zA-Z-]+)[""']", RegexOptions.CultureInvariant);
        private readonly object sync = new object();
        private readonly SpeechAsyncSemaphore generation = new SpeechAsyncSemaphore(1, 1);
        private readonly SpeechAsyncSemaphore delivery = new SpeechAsyncSemaphore(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource audioLifetime = new CancellationTokenSource();
        private readonly SpeechCallbackGuard callbacks = new SpeechCallbackGuard();
        private readonly HashSet<Work> requests = new HashSet<Work>();
        private readonly HashSet<UniTask> audioTasks = new HashSet<UniTask>();
        private readonly HashSet<UniTask> drainTasks = new HashSet<UniTask>();
        private readonly ISpeechRecognizer stt;
        private readonly ILlmService suppliedLlm;
        private readonly LlmConversation conversation;
        private readonly bool ownsConversation;
        private readonly ISpeechSynthesizer tts;
        private readonly ISpeechDetector vad;
        private readonly ISpeechRecognitionSource recognitionSource;
        private readonly Dictionary<string, SpeechRecognitionUpdate> recognitions = new Dictionary<string, SpeechRecognitionUpdate>();
        private readonly HashSet<string> closedRecognitions = new HashSet<string>();
        private readonly Queue<string> closedRecognitionOrder = new Queue<string>();
        private readonly IPipelinePerformanceRecorder performanceRecorder;
        private readonly ISpeechPipelineClock clock;
        private readonly bool ownsComponents;
        private SpeechPipelineOptions options;
        // One dialog's state. No registry, DB, worker-per-session, or adapter selection.
        private string contextId;
        private DateTimeOffset? lastConversationAt, previousRequestAt, timestampInsertedAt;
        private string previousRequestText, presentedTransactionId;
        private string[] previousImageUrls;
        private Work active;
        private long nextSequence, lastActivatedSequence;
        private bool controlling, disposing;
        private UniTask? controlTask, disposeTask;

        public string SessionId { get; }
        public event Func<SpeechPipelineResponse, UniTask> ResponseReceived;
        public event Action<Exception> Error;
        public event Action<SpeechRecognitionUpdate> RecognitionUpdated;

        public SpeechToSpeechPipeline(ISpeechRecognizer stt, ILlmService llm, ISpeechSynthesizer tts,
            SpeechPipelineOptions options = null, ISpeechDetector vad = null,
            IPipelinePerformanceRecorder performanceRecorder = null, ISpeechPipelineClock clock = null,
            bool ownsComponents = false, LlmHistoryFormat? historyFormat = null)
        {
            this.options = Snapshot(options ?? new SpeechPipelineOptions());
            this.stt = stt ?? throw new ArgumentNullException(nameof(stt));
            suppliedLlm = llm ?? throw new ArgumentNullException(nameof(llm));
            this.tts = tts ?? throw new ArgumentNullException(nameof(tts));
            this.vad = vad;
            recognitionSource = vad as ISpeechRecognitionSource;
            this.performanceRecorder = performanceRecorder;
            this.clock = clock ?? new SpeechPipelineClock();
            this.ownsComponents = ownsComponents;
            SessionId = this.options.SessionId;
            conversation = llm as LlmConversation;
            if (conversation == null)
            {
                conversation = historyFormat.HasValue ? new LlmConversation(llm, historyFormat.Value) : new LlmConversation(llm);
                ownsConversation = true;
            }
            var savedContext = conversation.Context.GetSnapshot().ContextId;
            if (savedContext != null && this.options.ContextId != null && savedContext != this.options.ContextId)
                throw new ArgumentException("The supplied conversation has a different context. Reset it before attaching it.");
            contextId = savedContext ?? this.options.ContextId ?? Guid.NewGuid().ToString("N");
            if (vad != null) { vad.SpeechDetected += OnSpeechDetectedAsync; vad.Error += ReportError; }
            if (recognitionSource != null) recognitionSource.RecognitionUpdated += OnRecognitionUpdated;
            if (stt is HttpSpeechRecognizerBase httpStt) httpStt.Error += ReportError;
        }

        public SpeechPipelineOptions GetOptions() { lock (sync) return options.Copy(); }
        public void UpdateOptions(SpeechPipelineOptions replacement)
        {
            var copy = Snapshot(replacement);
            lock (sync)
            {
                CheckAvailable();
                if (copy.SessionId != SessionId || copy.ContextId != options.ContextId)
                    throw new ArgumentException("SessionId is fixed. Use ResetAsync to change the context.");
                options = copy;
            }
        }
        public SpeechPipelineState GetState()
        {
            lock (sync) return new SpeechPipelineState
            {
                ContextId = contextId, ActiveTransactionId = active?.TransactionId,
                LastConversationUpdatedAt = lastConversationAt, PendingRequests = requests.Count
            };
        }

        public UniTask<SpeechPipelineResponse> InvokeAsync(SpeechPipelineRequest request, CancellationToken cancellationToken = default)
        { RejectReentry(); return StartInvoke(request, cancellationToken, false); }

        private UniTask<SpeechPipelineResponse> StartInvoke(SpeechPipelineRequest request, CancellationToken caller, bool detached, string recognitionId = null)
        {
            var copy = request?.Copy() ?? throw new ArgumentNullException(nameof(request));
            if (copy.SessionId != null && copy.SessionId != SessionId) throw new ArgumentException("This pipeline handles only its configured SessionId.");
            if (string.IsNullOrWhiteSpace(copy.TransactionId)) copy.TransactionId = Guid.NewGuid().ToString("N");
            if (double.IsNaN(copy.AudioDuration) || double.IsInfinity(copy.AudioDuration) || copy.AudioDuration < 0)
                throw new ArgumentOutOfRangeException(nameof(request.AudioDuration));
            Work work;
            lock (sync)
            {
                CheckAvailable(); caller.ThrowIfCancellationRequested();
                if (requests.Count >= options.MaxPendingRequests) throw new InvalidOperationException("The pipeline's pending request limit was reached.");
                if (requests.Any(item => item.TransactionId == copy.TransactionId)) throw new ArgumentException("An in-flight transaction already has this ID.");
                if (copy.ContextId != null && copy.ContextId != contextId) throw new ArgumentException("Reset the pipeline before changing ContextId.");
                copy.SessionId = SessionId; copy.ContextId = contextId;
                work = new Work
                {
                    Request = copy, TransactionId = copy.TransactionId, ContextId = contextId, RecognitionId = recognitionId,
                    Sequence = ++nextSequence, Settings = options.Copy(), Caller = caller,
                    WaitInQueue = copy.WaitInQueue,
                    Source = CancellationTokenSource.CreateLinkedTokenSource(caller, lifetime.Token),
                    Previous = copy.WaitInQueue ? SpeechAsync.WhenAll(requests.Select(item => (UniTask)item.Completion.Task)) : UniTask.CompletedTask,
                    StartTime = clock.ElapsedSeconds
                };
                try { work.Performance = NewPerformance(work); }
                catch { work.Source.Dispose(); throw; }
                requests.Add(work);
            }
            if (detached) _ = SpeechAsync.Run(() => RunWorkAsync(work));
            else _ = RunWorkAsync(work);
            return work.Completion.Task;
        }

        private async UniTask RunWorkAsync(Work work)
        {

            SpeechPipelineResponse terminal = null;
            bool callerCanceled = false;
            using var timeout = SpeechAsync.Timeout(work.Source, TimeSpan.FromSeconds(work.Settings.InvokeTimeoutSeconds));
            try
            {
                await EmitAsync(Response(work, SpeechPipelineResponseType.Accepted, new JObject { ["block_barge_in"] = work.Request.BlockBargeIn }));
                var token = work.Source.Token;
                token.ThrowIfCancellationRequested();
                if (work.Settings.OnAcceptedAsync != null) await callbacks.InvokeAsync(() => work.Settings.OnAcceptedAsync(work.Request, token));
                FixIdentity(work);
                try { await WaitAsync(work.Previous, token); }
                catch when (!token.IsCancellationRequested) { /* Earlier request failures do not poison the queue. */ }
                token.ThrowIfCancellationRequested();
                terminal = await ProcessAsync(work, token);
            }
            catch (OperationCanceledException)
            {
                callerCanceled = work.Caller.IsCancellationRequested;
                var reason = work.CancelReason;
                if (!callerCanceled && reason == null)
                    terminal = ErrorResponse(work, work.Source.IsCancellationRequested ? "timeout" : "component_canceled");
                else terminal = Response(work, SpeechPipelineResponseType.Canceled, new JObject { ["reason"] = reason ?? "caller_canceled" });
            }
            catch (Exception error)
            {
                ReportError(error);
                terminal = ErrorResponse(work, error is LlmServiceException ? "llm_error" : error is SpeechSynthesisException ? "tts_error" : "pipeline_error");
            }
            try
            {
                // Terminal/control notifications are always delivered, even when this turn has been superseded.
                // The original transaction ID lets the frontend distinguish these from the new turn.
                await EmitAsync(terminal, work, terminal: true);
            }
            catch (Exception error) { ReportError(error); }
            try
            {
                work.Performance.ContextId = work.ContextId;
                work.Performance.TotalTime = Elapsed(work);
                work.Performance.Status = terminal.Type.ToString().ToLowerInvariant();
                work.Performance.ErrorCode = (string)terminal.Metadata?["error_code"];
                if (performanceRecorder != null) await callbacks.InvokeAsync(() => performanceRecorder.RecordAsync(work.Performance.Copy(), lifetime.Token));
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) { ReportError(error); }
            finally
            {
                lock (sync)
                {
                    if (ReferenceEquals(active, work)) active = null;
                    requests.Remove(work);
                    // Completion and removal share the lock, so Dispose cannot miss work still using owned resources.
                    work.Source.Dispose();
                    if (callerCanceled || (terminal.Type == SpeechPipelineResponseType.Canceled && work.Caller.IsCancellationRequested))
                        work.Completion.TrySetCanceled();
                    else work.Completion.TrySetResult(terminal.Copy());
                }

            }
        }

        private async UniTask<SpeechPipelineResponse> ProcessAsync(Work work, CancellationToken token)
        {
            var request = work.Request;
            string recognized = request.Text;
            if (string.IsNullOrEmpty(recognized) && request.AudioData?.Length > 0)
            {
                var result = await callbacks.InvokeAsync(() => stt.RecognizeAsync(SessionId, request.AudioData, token));
                token.ThrowIfCancellationRequested();
                recognized = result?.Text;
                if (string.IsNullOrWhiteSpace(recognized)) return Response(work, SpeechPipelineResponseType.Canceled, new JObject { ["reason"] = "no_speech" });
            }
            request.Text = recognized ?? string.Empty;
            if (work.Settings.ValidateRequestAsync != null)
            {
                var reason = await callbacks.InvokeAsync(() => work.Settings.ValidateRequestAsync(request, token));
                FixIdentity(work); token.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(reason)) return Response(work, SpeechPipelineResponseType.Canceled, new JObject { ["reason"] = reason });
            }
            work.Performance.SttTime = Elapsed(work);
            if (string.IsNullOrWhiteSpace(request.Text) && request.ImageUrls.Length == 0)
                return Response(work, SpeechPipelineResponseType.Canceled, new JObject { ["reason"] = "empty_request" });
            Work[] superseded;
            lock (sync)
            {
                token.ThrowIfCancellationRequested();
                if (work.Sequence < lastActivatedSequence) return Response(work, SpeechPipelineResponseType.Canceled, new JObject { ["reason"] = "superseded" });
                var now = clock.UtcNow;
                var awake = work.Settings.Wakewords.Length == 0 ||
                    (lastConversationAt.HasValue && (now - lastConversationAt.Value).TotalSeconds < work.Settings.WakewordTimeoutSeconds) ||
                    work.Settings.Wakewords.Any(word => (request.Text ?? "").IndexOf(word, StringComparison.Ordinal) >= 0);
                if (!awake) return Response(work, SpeechPipelineResponseType.Canceled, new JObject { ["reason"] = "asleep" });
                if (work.Settings.MergeRequestThresholdSeconds > 0 && request.AllowMerge)
                {
                    if (previousRequestAt.HasValue && (now - previousRequestAt.Value).TotalSeconds < work.Settings.MergeRequestThresholdSeconds)
                    {
                        var oldText = previousRequestText ?? "";
                        if (work.Settings.MergeRequestPrefix.Length > 0) oldText = oldText.Replace(work.Settings.MergeRequestPrefix, "");
                        request.Text = work.Settings.MergeRequestPrefix + oldText + "\n" + request.Text;
                        if (request.ImageUrls.Length == 0) request.ImageUrls = (string[])previousImageUrls.Clone();
                    }
                    previousRequestAt = now; previousRequestText = request.Text; previousImageUrls = (string[])request.ImageUrls.Clone();
                }
                if (work.Settings.TimestampIntervalSeconds > 0 && (!timestampInsertedAt.HasValue ||
                    (now - timestampInsertedAt.Value).TotalSeconds > work.Settings.TimestampIntervalSeconds))
                {
                    request.Text = work.Settings.TimestampPrefix + TimeZoneInfo.ConvertTime(now, work.Settings.TimestampTimeZone)
                        .ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n\n" + request.Text;
                    timestampInsertedAt = now;
                }
                lastActivatedSequence = work.Sequence;
                active = work;
                superseded = requests.Where(item => item.Sequence < work.Sequence).ToArray();
            }
            foreach (var old in superseded) Cancel(old, "superseded");
            // Let canceled provider callbacks unwind before reusing the conversation/TTS instances.
            await generation.WaitAsync(token);
            try
            {
                EnsureActive(work, token);
                if (!work.WaitInQueue) await StopPlaybackAsync(work);
                work.Performance.StopResponseTime = Elapsed(work);
                await EmitAsync(Response(work, SpeechPipelineResponseType.Start, new JObject
                { ["request_text"] = request.Text, ["recognized_text"] = recognized }), work);
                if (work.Settings.BeforeLlmAsync != null) await callbacks.InvokeAsync(() => work.Settings.BeforeLlmAsync(request, token));
                FixIdentity(work); EnsureActive(work, token);
                work.Performance.BeforeLlmTime = Elapsed(work);
                var responseText = new StringBuilder(); var voiceText = new StringBuilder();
                var language = (string)null; var firstChunk = true; var beforeTtsCalled = false;
                var result = await callbacks.InvokeAsync(() => conversation.ChatAsync(new LlmRequest
                {
                    SessionId = SessionId, ContextId = work.ContextId, UserId = request.UserId, Channel = request.Channel,
                    Text = request.Text, ImageUrls = request.ImageUrls,
                    SystemPromptParameters = request.SystemPromptParameters, Parameters = request.LlmParameters
                }, async chunk =>
                {
                    EnsureActive(work, token);
                    if (chunk.Error != null) throw new LlmServiceException(chunk.Error);
                    if (chunk.IsFinal) return; // LLM completion is not a second speech chunk.
                    if (chunk.IsRecovery && string.IsNullOrEmpty(chunk.Text) && string.IsNullOrEmpty(chunk.VoiceText) &&
                        chunk.ToolCall == null && chunk.StructuredContent == null) return;
                    work.Performance.LlmFirstChunkTime = work.Performance.LlmFirstChunkTime ?? Elapsed(work);
                    if (chunk.ToolCall != null)
                    {
                        var tool = Response(work, SpeechPipelineResponseType.ToolCall);
                        tool.ToolCall = chunk.ToolCall.Copy(); tool.StructuredContent = (JObject)chunk.StructuredContent?.DeepClone();
                        await EmitAsync(tool, work);
                        return;
                    }
                    if (!string.IsNullOrEmpty(chunk.VoiceText))
                    {
                        work.Performance.LlmFirstVoiceChunkTime = work.Performance.LlmFirstVoiceChunkTime ?? Elapsed(work);
                        if (!beforeTtsCalled)
                        {
                            beforeTtsCalled = true;
                            if (work.Settings.BeforeTtsAsync != null) await callbacks.InvokeAsync(() => work.Settings.BeforeTtsAsync(request, token));
                            FixIdentity(work); EnsureActive(work, token);
                        }
                    }
                    work.Performance.LlmTime = Elapsed(work);
                    var match = LanguagePattern.Match(chunk.Text ?? "");
                    if (match.Success) language = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    var parsed = work.Settings.ProcessLlmChunkAsync == null ? null :
                        await callbacks.InvokeAsync(() => work.Settings.ProcessLlmChunkAsync(request, CopyChunk(chunk), token));
                    FixIdentity(work); EnsureActive(work, token);
                    byte[] audio = null;
                    if (!request.SkipTts && !string.IsNullOrWhiteSpace(chunk.VoiceText))
                    {
                        audio = await callbacks.InvokeAsync(() => tts.SynthesizeAsync(new SpeechSynthesisRequest
                        {
                            Text = chunk.VoiceText, Language = language,
                            StyleInfo = new JObject { ["styled_text"] = chunk.Text, ["info"] = parsed?.DeepClone() ?? new JObject() }
                        }, token));
                        EnsureActive(work, token);
                        if (audio?.Length > 0)
                        {
                            work.Performance.TtsFirstChunkTime = work.Performance.TtsFirstChunkTime ?? Elapsed(work);
                            work.Performance.TtsTime = Elapsed(work);
                        }
                    }
                    var response = Response(work, SpeechPipelineResponseType.Chunk, new JObject
                    { ["is_first_chunk"] = firstChunk, ["is_guardrail_triggered"] = !string.IsNullOrEmpty(chunk.GuardrailName) });
                    response.Text = chunk.Text; response.VoiceText = chunk.VoiceText; response.Language = language;
                    response.AudioData = (byte[])audio?.Clone(); response.StructuredContent = (JObject)chunk.StructuredContent?.DeepClone();
                    await EmitAsync(response, work);
                    responseText.Append(chunk.Text); voiceText.Append(chunk.VoiceText); firstChunk = false;
                }, token));
                EnsureActive(work, token);
                if (result.Error != null) throw new LlmServiceException(result.Error);
                if (result.InputItems?.Count > 0) lock (sync) lastConversationAt = clock.UtcNow;
                var final = Response(work, SpeechPipelineResponseType.Final);
                final.Text = responseText.ToString(); final.VoiceText = voiceText.ToString();
                if (work.Settings.OnFinishAsync != null) await callbacks.InvokeAsync(() => work.Settings.OnFinishAsync(request, final, token));
                final.Type = SpeechPipelineResponseType.Final;
                final.SessionId = SessionId; final.ContextId = work.ContextId; final.TransactionId = work.TransactionId;
                EnsureActive(work, token);
                return final;
            }
            finally { generation.Release(); }
        }

        private async UniTask EmitAsync(SpeechPipelineResponse response, Work current = null, bool terminal = false)
        {
            await delivery.WaitAsync();
            try
            {
                if (terminal && response.Type == SpeechPipelineResponseType.Final)
                {
                    lock (sync)
                    {
                        if (!ReferenceEquals(active, current) || current.Source.IsCancellationRequested)
                        {
                            var reason = current.CancelReason;
                            if (!ReferenceEquals(active, current)) reason = reason ?? "superseded";
                            var timedOut = reason == null && !current.Caller.IsCancellationRequested && current.Source.IsCancellationRequested;
                            response.Type = timedOut ? SpeechPipelineResponseType.Error : SpeechPipelineResponseType.Canceled;
                            response.Text = response.VoiceText = null;
                            response.Metadata = timedOut ? new JObject { ["error_code"] = "timeout", ["error"] = "Speech pipeline processing failed." }
                                : new JObject { ["reason"] = reason ?? "caller_canceled" };
                        }
                    }
                }
                else if (current != null && !terminal) EnsureActive(current, current.Source.Token);
                lock (sync)
                {
                    if (response.Type == SpeechPipelineResponseType.Start)
                    {
                        presentedTransactionId = response.TransactionId;
                        CloseRecognition(current?.RecognitionId, SpeechRecognitionUpdateKind.Confirmed,
                            (string)response.Metadata?["recognized_text"], response.TransactionId);
                    }
                    else if (terminal)
                        CloseRecognition(current?.RecognitionId, SpeechRecognitionUpdateKind.Canceled, transactionId: response.TransactionId);
                }
                var handlers = ResponseReceived;
                if (handlers != null)
                    foreach (Func<SpeechPipelineResponse, UniTask> handler in handlers.GetInvocationList())
                        await callbacks.InvokeAsync(() => handler(response.Copy()));
            }
            finally { delivery.Release(); }
        }

        private async UniTask StopPlaybackAsync(Work current = null)
        {
            await delivery.WaitAsync();
            try
            {
                if (current != null) EnsureActive(current, current.Source.Token);
                string stopped, context;
                lock (sync) { stopped = presentedTransactionId; context = contextId; presentedTransactionId = null; }
                var response = new SpeechPipelineResponse
                { Type = SpeechPipelineResponseType.Stop, SessionId = SessionId, ContextId = context, TransactionId = stopped };
                var handlers = ResponseReceived;
                if (handlers != null)
                    foreach (Func<SpeechPipelineResponse, UniTask> handler in handlers.GetInvocationList())
                        await callbacks.InvokeAsync(() => handler(response.Copy()));
            }
            finally { delivery.Release(); }
        }

        public UniTask ProcessAudioSamplesAsync(byte[] samples, CancellationToken cancellationToken = default)
        {
            RejectReentry();
            if (vad == null) throw new InvalidOperationException("No speech detector is attached.");
            var copy = (byte[])(samples ?? throw new ArgumentNullException(nameof(samples))).Clone();
            CancellationTokenSource source;
            var completion = new SpeechCompletionSource<bool>();
            var activeTask = completion.Task.AsUniTask();
            lock (sync)
            {
                CheckAvailable(); cancellationToken.ThrowIfCancellationRequested();
                source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token, audioLifetime.Token);
                audioTasks.Add(activeTask);
            }
            _ = RunAudioAsync(copy, source, completion, activeTask);
            return completion.Task;
        }
        private async UniTask RunAudioAsync(byte[] samples, CancellationTokenSource source, SpeechCompletionSource<bool> completion, UniTask activeTask)
        {
            try { await callbacks.InvokeAsync(() => vad.ProcessSamplesAsync(samples, SessionId, source.Token)); completion.TrySetResult(true); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { source.Dispose(); lock (sync) audioTasks.Remove(activeTask); }
        }
        private UniTask OnSpeechDetectedAsync(SpeechDetectionResult result)
        {
            if (result.SessionId != SessionId) return UniTask.CompletedTask;
            lock (sync) if (disposing || controlling) return UniTask.CompletedTask;
            try
            {
                // Register before returning to VAD; execution does not inherit VAD's callback/reentry guard.
                _ = StartInvoke(new SpeechPipelineRequest
                {
                    SessionId = SessionId, Text = result.Text, AudioData = result.Audio, AudioDuration = result.RecordedDuration,
                    Metadata = result.Metadata == null ? null : JObject.FromObject(result.Metadata)
                }, CancellationToken.None, true, result.RecognitionId);
            }
            catch (Exception error) { lock (sync) if (disposing || controlling) return UniTask.CompletedTask; ReportError(error); }
            return UniTask.CompletedTask;
        }

        private void OnRecognitionUpdated(SpeechRecognitionUpdate update)
        {
            if (update == null || string.IsNullOrEmpty(update.RecognitionId) || update.SessionId != SessionId) return;
            lock (sync)
            {
                if (disposing || controlling || closedRecognitions.Contains(update.RecognitionId)) return;
                if (update.Kind == SpeechRecognitionUpdateKind.Canceled)
                {
                    CloseRecognition(update.RecognitionId, SpeechRecognitionUpdateKind.Canceled);
                    return;
                }
                // A detector's confirmation is not yet a validated/awake pipeline request.
                // Keep it pending until Start supplies the final request identity.
                if (update.Kind == SpeechRecognitionUpdateKind.Confirmed)
                {
                    if (recognitions.TryGetValue(update.RecognitionId, out var completed))
                        completed.Kind = SpeechRecognitionUpdateKind.Confirmed;
                    return;
                }
                // Activity never creates a recognition or replaces its last transcript. A
                // detector's terminal update also ends activity while request validation runs.
                if (update.Kind == SpeechRecognitionUpdateKind.Activity)
                {
                    if (recognitions.TryGetValue(update.RecognitionId, out var observed) &&
                        observed.Kind != SpeechRecognitionUpdateKind.Confirmed)
                        PublishRecognition(update);
                    return;
                }
                if (recognitions.TryGetValue(update.RecognitionId, out var terminal) &&
                    terminal.Kind == SpeechRecognitionUpdateKind.Confirmed) return;
                var now = clock.UtcNow;
                var awake = options.Wakewords.Length == 0 ||
                    (lastConversationAt.HasValue && (now - lastConversationAt.Value).TotalSeconds < options.WakewordTimeoutSeconds) ||
                    options.Wakewords.Any(word => (update.Text ?? "").IndexOf(word, StringComparison.Ordinal) >= 0);
                if (!awake)
                {
                    if (recognitions.TryGetValue(update.RecognitionId, out var previous))
                    {
                        recognitions.Remove(update.RecognitionId);
                        previous.Kind = SpeechRecognitionUpdateKind.Canceled;
                        PublishRecognition(previous);
                    }
                    return;
                }
                var copy = update.Copy();
                recognitions[copy.RecognitionId] = copy;
                PublishRecognition(copy);
            }
        }

        // Called under sync: control cannot complete and then receive a partial from this generation.
        private void CloseRecognition(string recognitionId, SpeechRecognitionUpdateKind kind, string text = null, string transactionId = null)
        {
            if (recognitionId == null || closedRecognitions.Contains(recognitionId)) return;
            recognitions.TryGetValue(recognitionId, out var update);
            recognitions.Remove(recognitionId);
            closedRecognitions.Add(recognitionId);
            closedRecognitionOrder.Enqueue(recognitionId);
            if (closedRecognitionOrder.Count > 256) closedRecognitions.Remove(closedRecognitionOrder.Dequeue());
            if (update == null && kind == SpeechRecognitionUpdateKind.Canceled) return;
            update = update ?? new SpeechRecognitionUpdate { RecognitionId = recognitionId, SessionId = SessionId };
            update.Kind = kind;
            update.TransactionId = transactionId;
            if (text != null) update.Text = text;
            update.IsSpeechActive = null;
            update.AudioDurationSeconds = null;
            update.ObservedAtSeconds = 0;
            PublishRecognition(update);
        }

        private void CancelRecognitions()
        {
            foreach (var id in recognitions.Keys.ToArray()) CloseRecognition(id, SpeechRecognitionUpdateKind.Canceled);
        }

        private void PublishRecognition(SpeechRecognitionUpdate update)
        {
            var handlers = RecognitionUpdated;
            if (handlers == null) return;
            var snapshot = update.Copy();
            foreach (Action<SpeechRecognitionUpdate> handler in handlers.GetInvocationList())
                try { callbacks.Invoke(() => handler(snapshot.Copy())); }
                catch (Exception error) { ReportError(error); }
        }

        public UniTask InterruptAsync(CancellationToken cancellationToken = default) => BeginControl(false, null, cancellationToken);
        public UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default) => BeginControl(true, contextId, cancellationToken);
        private UniTask BeginControl(bool reset, string nextContext, CancellationToken token)
        {
            RejectReentry();
            if (nextContext != null && string.IsNullOrWhiteSpace(nextContext)) throw new ArgumentException("ContextId must be nonempty when supplied.");
            var completion = new SpeechCompletionSource<bool>();
            Work[] pending; UniTask[] inputs; CancellationTokenSource oldAudio;
            lock (sync)
            {
                CheckAvailable(); token.ThrowIfCancellationRequested();
                controlling = true; controlTask = completion.Task;
                CancelRecognitions();
                pending = requests.ToArray(); inputs = audioTasks.ToArray(); oldAudio = audioLifetime;
            }
            _ = RunControlAsync(reset, nextContext, pending, inputs, oldAudio, completion);
            // Once accepted, finish the cancellation/reset boundary even if the caller stops waiting.
            return WaitAsync(completion.Task, token);
        }
        private async UniTask RunControlAsync(bool reset, string nextContext, Work[] pending, UniTask[] inputs,
            CancellationTokenSource oldAudio, SpeechCompletionSource<bool> completion)
        {
            Exception failure = null;
            try
            {
                foreach (var work in pending) Cancel(work, reset ? "reset" : "interrupted");
                try { oldAudio.Cancel(); } catch (Exception error) { ReportError(error); }
                try { await SpeechAsync.WhenAll(inputs.Concat(pending.Select(work => (UniTask)work.Completion.Task))); } catch { }
                if (vad != null)
                {
                    await callbacks.InvokeAsync(() => vad.ResetSessionAudioStateAsync(SessionId, cancellationToken: lifetime.Token));
                    await WaitAsync(callbacks.InvokeAsync(vad.DrainAsync), lifetime.Token);
                }
                await StopPlaybackAsync();
                if (reset)
                {
                    await callbacks.InvokeAsync(() => conversation.ResetAsync());
                    lock (sync)
                    {
                        contextId = nextContext ?? Guid.NewGuid().ToString("N");
                        lastConversationAt = previousRequestAt = timestampInsertedAt = null;
                        previousRequestText = null; previousImageUrls = null;
                    }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (disposing) { }
            catch (Exception error) { failure = error; }
            finally
            {
                lock (sync)
                {
                    oldAudio.Dispose(); audioLifetime = new CancellationTokenSource(); controlling = false; controlTask = null;
                    if (failure == null) completion.TrySetResult(true); else completion.TrySetException(failure);
                }

            }
        }

        public UniTask DrainAsync()
        {
            RejectReentry();
            UniTask[] inputs; UniTask? control;
            var completion = new SpeechCompletionSource<bool>();
            var activeTask = completion.Task.AsUniTask();
            lock (sync)
            {
                ThrowIfDisposed(); inputs = audioTasks.ToArray(); control = controlTask;
                drainTasks.Add(activeTask);
            }
            _ = RunDrainAsync(inputs, control, completion, activeTask);
            return completion.Task;
        }
        private async UniTask RunDrainAsync(UniTask[] inputs, UniTask? control, SpeechCompletionSource<bool> completion, UniTask activeTask)
        {
            try
            {
                try { await SpeechAsync.WhenAll(inputs); } catch { }
                if (vad != null) await WaitAsync(callbacks.InvokeAsync(vad.DrainAsync), lifetime.Token);
                UniTask[] turns;
                lock (sync) turns = requests.Select(work => (UniTask)work.Completion.Task).ToArray();
                try { await SpeechAsync.WhenAll(turns); } catch { }
                if (control != null) await control.Value;
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { lock (sync) drainTasks.Remove(activeTask); }
        }

        public UniTask DisposeAsync()
        {
            RejectReentry();
            var completion = new SpeechCompletionSource<bool>();
            Work[] pending; UniTask[] inputs; UniTask? control;
            lock (sync)
            {
                if (disposeTask != null) return disposeTask.Value;
                disposing = true; disposeTask = completion.Task;
                CancelRecognitions();
                pending = requests.ToArray(); inputs = audioTasks.Concat(drainTasks).ToArray(); control = controlTask;
            }
            if (vad != null) { vad.SpeechDetected -= OnSpeechDetectedAsync; vad.Error -= ReportError; }
            if (recognitionSource != null) recognitionSource.RecognitionUpdated -= OnRecognitionUpdated;
            if (stt is HttpSpeechRecognizerBase httpStt) httpStt.Error -= ReportError;
            _ = DisposeCoreAsync(pending, inputs, control, completion);
            return disposeTask.Value;
        }
        private async UniTask DisposeCoreAsync(Work[] pending, UniTask[] inputs, UniTask? control, SpeechCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            foreach (var work in pending) Cancel(work, "disposed");
            try { lifetime.Cancel(); } catch (Exception error) { errors.Add(error); }
            // VAD may own partial-STT/gate tasks that Drain is waiting for. Signal its lifetime before waiting.
            UniTask? vadDisposal = null;
            if (ownsComponents && vad != null)
                try { vadDisposal = callbacks.InvokeAsync(vad.DisposeAsync); } catch (Exception error) { errors.Add(error); }
            try { await SpeechAsync.WhenAll(inputs.Concat(pending.Select(work => (UniTask)work.Completion.Task))); } catch { }
            if (control != null) try { await control.Value; } catch (Exception error) { errors.Add(error); }
            if (vadDisposal != null) try { await vadDisposal.Value; } catch (Exception error) { errors.Add(error); }
            try { await StopPlaybackAsync(); } catch (Exception error) { errors.Add(error); }
            if (ownsConversation) try { await callbacks.InvokeAsync(conversation.DisposeAsync); } catch (Exception error) { errors.Add(error); }
            if (ownsComponents)
            {
                var disposed = new List<object>();
                if (vad != null) disposed.Add(vad);
                foreach (var component in new object[] { vad, stt, suppliedLlm, tts })
                {
                    if (component == null || disposed.Any(item => ReferenceEquals(item, component))) continue;
                    disposed.Add(component);
                    try { await callbacks.InvokeAsync(() => DisposeComponentAsync(component)); } catch (Exception error) { errors.Add(error); }
                }
            }
            lifetime.Dispose(); audioLifetime.Dispose(); generation.Dispose(); delivery.Dispose();
            if (errors.Count == 0) completion.TrySetResult(true); else completion.TrySetException(new AggregateException(errors));

        }

        private static UniTask DisposeComponentAsync(object component)
        {
            if (component is ISpeechDetector detector) return detector.DisposeAsync();
            if (component is ISpeechSynthesizer synthesizer) return synthesizer.DisposeAsync();
            if (component is ILlmService llm) return llm.DisposeAsync();
            if (component is SpeechRecognizerBase recognizer) return recognizer.DisposeAsync();
            if (component is IAsyncDisposable asyncDisposable) return SpeechAsync.FromTask(asyncDisposable.DisposeAsync().AsTask());
            (component as IDisposable)?.Dispose(); return UniTask.CompletedTask;
        }
        private SpeechPipelineResponse Response(Work work, SpeechPipelineResponseType type, JObject metadata = null) => new SpeechPipelineResponse
        {
            Type = type, SessionId = SessionId, ContextId = work.ContextId, TransactionId = work.TransactionId,
            UserId = work.Request.UserId, Channel = work.Request.Channel, Metadata = metadata
        };
        private SpeechPipelineResponse ErrorResponse(Work work, string code) => Response(work, SpeechPipelineResponseType.Error,
            new JObject { ["error_code"] = code, ["error"] = "Speech pipeline processing failed." });
        private void FixIdentity(Work work)
        {
            work.Request.SessionId = SessionId; work.Request.ContextId = work.ContextId;
            work.Request.TransactionId = work.TransactionId; work.Request.WaitInQueue = work.WaitInQueue;
        }
        private void EnsureActive(Work work, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (sync) if (!ReferenceEquals(active, work))
            {
                work.CancelReason = work.CancelReason ?? "superseded";
                throw new OperationCanceledException(token);
            }
        }
        private void Cancel(Work work, string reason)
        {
            lock (sync) { if (!requests.Contains(work)) return; if (work.CancelReason == null) work.CancelReason = reason; }
            try { work.Source.Cancel(); } catch (ObjectDisposedException) { } catch (Exception error) { ReportError(error); }
        }
        private double Elapsed(Work work) => Math.Max(0, clock.ElapsedSeconds - work.StartTime);
        private PipelinePerformanceRecord NewPerformance(Work work)
        {
            var metadata = work.Request.Metadata?["vad_performance"] as JObject;
            return new PipelinePerformanceRecord
            {
                TransactionId = work.TransactionId, SessionId = SessionId, ContextId = work.ContextId,
                UserId = work.Request.UserId, Channel = work.Request.Channel, StartedAt = clock.UtcNow,
                SttName = stt.GetType().Name, LlmName = suppliedLlm.GetType().Name, TtsName = tts.GetType().Name,
                VoiceLength = work.Request.AudioDuration, SpeechEndAt = metadata?.Value<DateTimeOffset?>("speech_end_at"),
                SilenceThresholdTime = metadata?.Value<double?>("silence_threshold_time"),
                SttAfterThresholdTime = metadata?.Value<double?>("stt_after_threshold_time"),
                TurnEndGateTime = metadata?.Value<double?>("turn_end_gate_time"), TurnEndGateHeld = metadata?.Value<bool?>("turn_end_gate_held")
            };
        }
        private static LlmResponse CopyChunk(LlmResponse source) => new LlmResponse
        {
            ContextId = source.ContextId, Text = source.Text, VoiceText = source.VoiceText, ResponseId = source.ResponseId,
            IsFinal = source.IsFinal, IsRecovery = source.IsRecovery, GuardrailName = source.GuardrailName,
            ToolCall = source.ToolCall?.Copy(), StructuredContent = (JObject)source.StructuredContent?.DeepClone(), Error = source.Error
        };
        private static UniTask WaitAsync(UniTask task, CancellationToken token) => SpeechAsync.WaitAsync(task, token);
        private void ReportError(Exception error)
        {
            var handlers = Error;
            if (handlers != null) foreach (Action<Exception> handler in handlers.GetInvocationList()) try { callbacks.Invoke(() => handler(error)); } catch { }
        }
        private void CheckAvailable() { ThrowIfDisposed(); if (controlling) throw new InvalidOperationException("Wait for InterruptAsync/ResetAsync to finish before submitting more work."); }
        private void ThrowIfDisposed() { if (disposing) throw new ObjectDisposedException(nameof(SpeechToSpeechPipeline)); }
        private void RejectReentry() { callbacks.ThrowIfActive("Schedule pipeline control after its callback or hook returns."); }
        private static SpeechPipelineOptions Snapshot(SpeechPipelineOptions value)
        { var copy = value?.Copy() ?? throw new ArgumentNullException(nameof(value)); copy.Validate(); return copy; }
        private sealed class Work
        {
            internal SpeechPipelineRequest Request;
            internal SpeechPipelineOptions Settings;
            internal string TransactionId, ContextId, CancelReason, RecognitionId;
            internal long Sequence;
            internal bool WaitInQueue;
            internal double StartTime;
            internal CancellationToken Caller;
            internal CancellationTokenSource Source;
            internal UniTask Previous;
            internal PipelinePerformanceRecord Performance;
            internal readonly SpeechCompletionSource<SpeechPipelineResponse> Completion = new SpeechCompletionSource<SpeechPipelineResponse>();
        }
    }
}

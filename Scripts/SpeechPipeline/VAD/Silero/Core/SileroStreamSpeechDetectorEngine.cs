using ChatdollKit.SpeechPipeline.VAD;
// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
// See SpeechPipeline/README.ja.md for compatibility and lifecycle decisions.
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD.Filters;
using ChatdollKit.SpeechPipeline.VAD.TurnEndGates;

namespace ChatdollKit.SpeechPipeline.VAD.Silero
{
    /// <summary>
    /// Silero VAD with cumulative batch recognition at short pauses. Partial text
    /// replaces the previous hypothesis; this is not a streaming STT transport.
    /// </summary>
    public sealed class SileroStreamSpeechDetectorEngine : SileroSpeechDetectorEngine
    {
        public ISpeechRecognizer SpeechRecognizer { get; }
        public event Func<string, SileroStreamRecordingSession, UniTask> SpeechDetecting;
        public event Func<Exception, string, UniTask> SpeechRecognitionError;
        /// <summary>Returns a nonempty rejection reason to discard a final transcript.</summary>
        public Func<string, string> ValidateRecognizedText { get; set; }

        public SileroStreamSpeechDetectorEngine(
            ISileroVadModel model,
            ISpeechRecognizer speechRecognizer,
            SileroStreamSpeechDetectorOptions options = null,
            IEnumerable<IAudioFilter> audioFilters = null,
            IEnumerable<ITurnEndGate> turnEndGates = null,
            ISpeechDetectorClock clock = null)
            : base(model, options ?? new SileroStreamSpeechDetectorOptions(), audioFilters, turnEndGates, clock)
        {
            SpeechRecognizer = speechRecognizer ?? throw new ArgumentNullException(nameof(speechRecognizer));
        }

        public SileroStreamSpeechDetectorEngine(
            IReadOnlyList<ISileroVadModel> models,
            ISpeechRecognizer speechRecognizer,
            SileroStreamSpeechDetectorOptions options = null,
            IEnumerable<IAudioFilter> audioFilters = null,
            IEnumerable<ITurnEndGate> turnEndGates = null,
            ISpeechDetectorClock clock = null)
            : base(models, options ?? new SileroStreamSpeechDetectorOptions(), audioFilters, turnEndGates, clock)
        {
            SpeechRecognizer = speechRecognizer ?? throw new ArgumentNullException(nameof(speechRecognizer));
        }

        protected override RecordingSession CreateSession(string sessionId)
        {
            var options = (SileroSpeechDetectorOptions)Options;
            return new SileroStreamRecordingSession(sessionId, options.PrerollBufferCount,
                new SileroVadIterator(GetModel(sessionId), options.SampleRate, options.SpeechProbabilityThreshold));
        }

        public UniTask SetSpeechRecognizerAsync(string sessionId, ISpeechRecognizer speechRecognizer,
            CancellationToken cancellationToken = default)
        {
            if (speechRecognizer == null) throw new ArgumentNullException(nameof(speechRecognizer));
            return WithOperationAsync(() =>
            {
                var session = (SileroStreamRecordingSession)GetSessionCore(sessionId);
                lock (session.SyncRoot) session.SpeechRecognizerOverride = speechRecognizer;
                return UniTask.FromResult(true);
            }, cancellationToken);
        }

        public UniTask<ISpeechRecognizer> GetSpeechRecognizerAsync(string sessionId,
            CancellationToken cancellationToken = default)
        {
            return WithOperationAsync(() =>
            {
                RecordingSession existing;
                if (!TryGetSessionCore(sessionId, out existing)) return UniTask.FromResult(SpeechRecognizer);
                return UniTask.FromResult(GetSpeechRecognizer((SileroStreamRecordingSession)existing));
            }, cancellationToken);
        }

        public UniTask ClearSpeechRecognizerAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return WithOperationAsync(() =>
            {
                RecordingSession existing;
                if (TryGetSessionCore(sessionId, out existing))
                {
                    lock (existing.SyncRoot) ((SileroStreamRecordingSession)existing).SpeechRecognizerOverride = null;
                }
                return UniTask.FromResult(true);
            }, cancellationToken);
        }

        private ISpeechRecognizer GetSpeechRecognizer(SileroStreamRecordingSession session)
        {
            lock (session.SyncRoot) return session.SpeechRecognizerOverride ?? SpeechRecognizer;
        }

        protected override UniTask OnRecordingChunkAsync(SileroRecordingSession recordingSession,
            byte[] samples, bool voiced, double duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = (SileroStreamRecordingSession)recordingSession;
            lock (session.SyncRoot)
            {
                session.SegmentDuration += duration;
                if (voiced)
                {
                    session.SegmentSilenceDuration = 0;
                    session.SegmentFired = false;
                }
                else session.SegmentSilenceDuration += duration;

                var options = (SileroStreamSpeechDetectorOptions)Options;
                if (session.SegmentSilenceDuration >= options.SegmentSilenceThreshold &&
                    session.SegmentDuration > 0 && !session.SegmentFired)
                {
                    session.SegmentFired = true;
                    var audio = session.Buffer.ToArray();
                    var sequence = ++session.RecognitionSequence;
                    var cancellation = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken);
                    session.RecognitionCancellations.Add(cancellation);
                    session.PendingRecognitionCancellation = cancellation;
                    session.PendingRecognitionTask = TrackBackground(
                        () => RunSegmentRecognitionAsync(session, audio, sequence, cancellation));
                }
            }
            return UniTask.CompletedTask;
        }

        private async UniTask RunSegmentRecognitionAsync(SileroStreamRecordingSession session,
            byte[] audio, int sequence, CancellationTokenSource cancellation)
        {
            var token = cancellation.Token;
            try
            {
                var result = await InvokeExtensionAsync(() => GetSpeechRecognizer(session)
                    .RecognizeAsync(session.SessionId, audio, token));
                token.ThrowIfCancellationRequested();
                var text = result.Text;
                lock (session.SyncRoot)
                {
                    // Intentionally compare only sequence, including its reset to zero.
                    // This retains AIAvatarKit's documented late-result behavior.
                    if (string.IsNullOrEmpty(text) || sequence != session.RecognitionSequence) return;
                    session.LastRecognizedText = text;
                }
                var handlers = SpeechDetecting;
                if (handlers != null)
                {
                    foreach (Func<string, SileroStreamRecordingSession, UniTask> handler in handlers.GetInvocationList())
                        await InvokeCallbackAsync(() => handler(text, session));
                }
                CheckRecordingStarted(session);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                await ReportRecognitionErrorAsync(exception, session.SessionId);
            }
            finally
            {
                lock (session.SyncRoot)
                {
                    session.RecognitionCancellations.Remove(cancellation);
                    if (ReferenceEquals(session.PendingRecognitionCancellation, cancellation))
                        session.PendingRecognitionCancellation = null;
                }
                cancellation.Dispose();
            }
        }

        private async UniTask ReportRecognitionErrorAsync(Exception exception, string sessionId)
        {
            ReportError(exception);
            var handlers = SpeechRecognitionError;
            if (handlers == null) return;
            foreach (Func<Exception, string, UniTask> handler in handlers.GetInvocationList())
                await InvokeCallbackAsync(() => handler(exception, sessionId));
        }

        private async UniTask WaitPendingRecognitionAsync(SileroStreamRecordingSession session,
            CancellationToken cancellationToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, LifetimeToken))
                await WaitPendingRecognitionCoreAsync(session, linked.Token);
        }

        private async UniTask WaitPendingRecognitionCoreAsync(SileroStreamRecordingSession session,
            CancellationToken cancellationToken)
        {
            UniTask? task;
            double? startedAt;
            lock (session.SyncRoot)
            {
                task = session.PendingRecognitionTask;
                startedAt = session.SilenceThresholdReachedAt.HasValue ? (double?)Clock.ElapsedSeconds : null;
            }
            if (task == null) return;
            try
            {
                await SpeechAsync.WaitAsync(task.Value, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) when (!(exception is OperationCanceledException)) { ReportError(exception); }
            finally
            {
                if (startedAt.HasValue)
                {
                    lock (session.SyncRoot)
                        session.SttAfterThresholdTime = (session.SttAfterThresholdTime ?? 0) + Clock.ElapsedSeconds - startedAt.Value;
                }
            }
        }

        protected override void MarkSilenceThresholdReached(SileroRecordingSession session)
        {
            base.MarkSilenceThresholdReached(session);
            lock (session.SyncRoot)
                if (!session.SttAfterThresholdTime.HasValue) session.SttAfterThresholdTime = 0;
        }

        protected override async UniTask<bool> ShouldEndTurnWithGateAsync(SileroRecordingSession session,
            double recordedDuration, CancellationToken cancellationToken)
        {
            if (HasTurnEndGates)
                await WaitPendingRecognitionAsync((SileroStreamRecordingSession)session, cancellationToken);
            return await base.ShouldEndTurnWithGateAsync(session, recordedDuration, cancellationToken);
        }

        protected override async UniTask CompleteRecordingAsync(SileroRecordingSession recordingSession,
            double recordedDuration, bool atMaximum, CancellationToken cancellationToken)
        {
            var session = (SileroStreamRecordingSession)recordingSession;
            await WaitPendingRecognitionAsync(session, cancellationToken);
            string finalText;
            lock (session.SyncRoot) finalText = session.LastRecognizedText;
            // At maximum duration the source intentionally drops audio if no
            // partial transcript exists. Only silence completion has this fallback.
            if (!atMaximum && finalText == null)
            {
                var startedAt = Clock.ElapsedSeconds;
                try
                {
                    byte[] audio;
                    lock (session.SyncRoot) audio = session.Buffer.ToArray();
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, LifetimeToken))
                    {
                        var result = await InvokeExtensionAsync(() => GetSpeechRecognizer(session).RecognizeAsync(
                            session.SessionId, audio, linked.Token));
                        linked.Token.ThrowIfCancellationRequested();
                        finalText = result.Text;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || LifetimeToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    await ReportRecognitionErrorAsync(exception, session.SessionId);
                }
                finally
                {
                    lock (session.SyncRoot)
                        session.SttAfterThresholdTime = (session.SttAfterThresholdTime ?? 0) + Clock.ElapsedSeconds - startedAt;
                }
            }
            if (string.IsNullOrEmpty(finalText)) return;
            var validate = ValidateRecognizedText;
            if (validate != null && !string.IsNullOrEmpty(InvokeExtension(() => validate(finalText)))) return;
            SpeechDetectionResult detection;
            lock (session.SyncRoot)
                detection = new SpeechDetectionResult(session.Buffer.ToArray(), finalText,
                    BuildVadPerformanceMetadata(session), recordedDuration, session.SessionId);
            PublishSpeechDetected(detection);
        }

        protected override void ResetSessionAudioStateCore(RecordingSession recordingSession, bool clearPreroll)
        {
            var session = (SileroStreamRecordingSession)recordingSession;
            CancellationTokenSource[] cancellations;
            lock (session.SyncRoot)
            {
                cancellations = new CancellationTokenSource[session.RecognitionCancellations.Count];
                session.RecognitionCancellations.CopyTo(cancellations);
            }
            foreach (var cancellation in cancellations)
            {
                try { cancellation.Cancel(); }
                catch (ObjectDisposedException) { /* The recognition completed concurrently. */ }
                catch (Exception exception) { ReportError(exception); }
            }
            base.ResetSessionAudioStateCore(session, clearPreroll);
        }
    }
}

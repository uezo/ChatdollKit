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
    public sealed class SileroStreamSpeechDetectorEngine : SileroSpeechDetectorEngine, ISpeechRecognitionSource
    {
        public ISpeechRecognizer SpeechRecognizer { get; }
        public event Func<string, SileroStreamRecordingSession, UniTask> SpeechDetecting;
        public event Func<Exception, string, UniTask> SpeechRecognitionError;
        public event Action<SpeechRecognitionUpdate> RecognitionUpdated;
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
            var session = new SileroStreamRecordingSession(sessionId, options.PrerollBufferCount,
                new SileroVadIterator(GetModel(sessionId), options.SampleRate, options.SpeechProbabilityThreshold));
            session.RecognitionReset = () => CloseRecognition(session, SpeechRecognitionUpdateKind.Canceled);
            return session;
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

        private async UniTask<SpeechRecognitionResult> RecognizeCurrentAudioAsync(SileroStreamRecordingSession session,
            byte[] audio, long generation, CancellationToken token)
        {
            UniTask<SpeechRecognitionResult> recognition;
            CancellationToken audioToken;
            lock (AudioSync)
            {
                RequireCurrentAudio(generation, token);
                audioToken = AudioToken;
                recognition = SpeechAsync.Share(InvokeExtensionAsync(() => GetSpeechRecognizer(session)
                    .RecognizeAsync(session.SessionId, audio, token)));
            }
            // Observe the provider token so reset cancellation reaches it before
            // unwinding and disposing any of its linked token sources.
            try { return await SpeechAsync.WaitAsync(recognition, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested && !audioToken.IsCancellationRequested)
            {
                // Keep the existing completion wait for caller/lifetime cancellation.
                return await SpeechAsync.WaitAsync(recognition, audioToken);
            }
        }

        protected override async UniTask<bool> ProcessSamplesCoreAsync(byte[] samples,
            RecordingSession recordingSession, CancellationToken cancellationToken)
        {
            var session = (SileroStreamRecordingSession)recordingSession;
            var generation = CurrentInputGeneration;
            bool wasRecording;
            lock (session.SyncRoot) wasRecording = session.IsRecording;
            bool recording;
            try { recording = await base.ProcessSamplesCoreAsync(samples, session, cancellationToken); }
            catch
            {
                lock (session.SyncRoot) CloseRecognition(session, SpeechRecognitionUpdateKind.Canceled);
                throw;
            }
            if (!wasRecording && recording)
            {
                // The base handles the onset separately from OnRecordingChunkAsync.
                lock (AudioSync)
                lock (session.SyncRoot)
                {
                    RequireCurrentAudio(generation, cancellationToken);
                    session.RecognitionNotified = true;
                    PublishSpeechActivity(session, SpeechRecognitionUpdateKind.Started, true, SampleDuration(samples));
                }
            }
            return recording;
        }

        // Called under the session lock. These are audio facts; consumers decide when
        // to display them. Partial recognition does not end recording activity.
        private void PublishSpeechActivity(SileroStreamRecordingSession session,
            SpeechRecognitionUpdateKind kind, bool voiced, double duration)
        {
            if (session.RecognitionClosed || session.RecognitionId == null) return;
            PublishRecognition(new SpeechRecognitionUpdate
            {
                RecognitionId = session.RecognitionId, SessionId = session.SessionId,
                Kind = kind, IsSpeechActive = voiced, AudioDurationSeconds = duration
            });
        }

        protected override UniTask OnRecordingChunkAsync(SileroRecordingSession recordingSession,
            byte[] samples, bool voiced, double duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = (SileroStreamRecordingSession)recordingSession;
            lock (AudioSync)
            lock (session.SyncRoot)
            {
                RequireCurrentAudio(CurrentInputGeneration, cancellationToken);
                PublishSpeechActivity(session, SpeechRecognitionUpdateKind.Activity, voiced, duration);
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
                    var recognitionId = session.RecognitionId;
                    var generation = CurrentInputGeneration;
                    var cancellation = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, AudioToken);
                    session.RecognitionCancellations.Add(cancellation);
                    session.PendingRecognitionCancellation = cancellation;
                    session.PendingRecognitionTask = TrackBackground(
                        () => RunSegmentRecognitionAsync(session, audio, sequence, recognitionId, generation, cancellation));
                }
            }
            return UniTask.CompletedTask;
        }

        private async UniTask RunSegmentRecognitionAsync(SileroStreamRecordingSession session,
            byte[] audio, int sequence, string recognitionId, long generation, CancellationTokenSource cancellation)
        {
            var token = cancellation.Token;
            try
            {
                var result = await RecognizeCurrentAudioAsync(session, audio, generation, token);
                token.ThrowIfCancellationRequested();
                var text = result.Text;
                lock (AudioSync)
                lock (session.SyncRoot)
                {
                    RequireCurrentAudio(generation, token);
                    // Intentionally compare only sequence, including its reset to zero.
                    // This retains AIAvatarKit's documented late-result behavior.
                    if (string.IsNullOrEmpty(text) || sequence != session.RecognitionSequence) return;
                    session.LastRecognizedText = text;
                    // Keep legacy late-result behavior above, but never revive an old common recognition.
                    if (recognitionId == session.RecognitionId && !session.RecognitionClosed && !token.IsCancellationRequested)
                    {
                        session.RecognitionNotified = true;
                        PublishRecognition(new SpeechRecognitionUpdate
                        {
                            RecognitionId = recognitionId, SessionId = session.SessionId,
                            Text = text, Kind = SpeechRecognitionUpdateKind.Partial
                        });
                    }
                }
                var handlers = SpeechDetecting;
                if (handlers != null)
                {
                    foreach (Func<string, SileroStreamRecordingSession, UniTask> handler in handlers.GetInvocationList())
                    {
                        UniTask callback;
                        lock (AudioSync)
                        {
                            RequireCurrentAudio(generation, token);
                            callback = InvokeCallbackAsync(() => handler(text, session));
                        }
                        await callback;
                    }
                }
                lock (AudioSync)
                {
                    RequireCurrentAudio(generation, token);
                    CheckRecordingStarted(session);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || generation != Interlocked.Read(ref AudioGeneration)) { }
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
            var generation = CurrentInputGeneration;
            await WaitPendingRecognitionAsync(session, cancellationToken);
            string finalText;
            lock (AudioSync)
            lock (session.SyncRoot)
            {
                RequireCurrentAudio(generation, cancellationToken);
                finalText = session.LastRecognizedText;
            }
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
                        var result = await RecognizeCurrentAudioAsync(session, audio, generation, linked.Token);
                        linked.Token.ThrowIfCancellationRequested();
                        finalText = result.Text;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || LifetimeToken.IsCancellationRequested ||
                    generation != Interlocked.Read(ref AudioGeneration)) { throw; }
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
            lock (AudioSync)
            lock (session.SyncRoot)
            {
                RequireCurrentAudio(generation, cancellationToken);
                detection = new SpeechDetectionResult(session.Buffer.ToArray(), finalText,
                    BuildVadPerformanceMetadata(session), recordedDuration, session.SessionId, session.RecognitionId);
                CloseRecognition(session, SpeechRecognitionUpdateKind.Confirmed, finalText);
                PublishSpeechDetected(detection);
            }
        }

        private void RequireCurrentAudio(long generation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (generation != AudioGeneration) throw new OperationCanceledException(token);
        }

        private void CloseRecognition(SileroStreamRecordingSession session, SpeechRecognitionUpdateKind kind, string text = null)
        {
            if (session.RecognitionClosed || session.RecognitionId == null) return;
            session.RecognitionClosed = true;
            if (kind == SpeechRecognitionUpdateKind.Canceled && !session.RecognitionNotified) return;
            PublishRecognition(new SpeechRecognitionUpdate
            {
                RecognitionId = session.RecognitionId, SessionId = session.SessionId,
                Text = text ?? session.LastRecognizedText, Kind = kind
            });
        }

        private void PublishRecognition(SpeechRecognitionUpdate update)
        {
            var handlers = RecognitionUpdated;
            if (handlers == null) return;
            var snapshot = update.Copy();
            snapshot.ObservedAtSeconds = Clock.ElapsedSeconds;
            foreach (Action<SpeechRecognitionUpdate> handler in handlers.GetInvocationList())
                try { InvokeExtension(() => { handler(snapshot.Copy()); return true; }); }
                catch (Exception error) { ReportError(error); }
        }

        protected override void ResetSpeechInputCore(RecordingSession recordingSession, bool clearPreroll)
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
            base.ResetSpeechInputCore(session, clearPreroll);
        }
    }
}

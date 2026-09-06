using ChatdollKit.SpeechPipeline.VAD;
// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
// See SpeechPipeline/README.ja.md for compatibility and lifecycle decisions.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.VAD.Filters;
using ChatdollKit.SpeechPipeline.VAD.TurnEndGates;

namespace ChatdollKit.SpeechPipeline.VAD.Silero
{
    public class SileroSpeechDetectorEngine : SpeechDetectorBase
    {
        private readonly ISileroVadModel[] models;
        private readonly IAudioFilter[] audioFilters;
        private readonly TurnEndGateManager gateManager;
        protected SileroSpeechDetectorOptions SileroOptions => (SileroSpeechDetectorOptions)Options;
        protected bool HasTurnEndGates => gateManager.HasGates;

        public SileroSpeechDetectorEngine(ISileroVadModel model, SileroSpeechDetectorOptions options = null,
            IEnumerable<IAudioFilter> audioFilters = null, IEnumerable<ITurnEndGate> turnEndGates = null,
            ISpeechDetectorClock clock = null)
            : this(new[] { model }, options, audioFilters, turnEndGates, clock) { }

        public SileroSpeechDetectorEngine(IReadOnlyList<ISileroVadModel> models, SileroSpeechDetectorOptions options = null,
            IEnumerable<IAudioFilter> audioFilters = null, IEnumerable<ITurnEndGate> turnEndGates = null,
            ISpeechDetectorClock clock = null) : base(options ?? new SileroSpeechDetectorOptions(), clock)
        {
            if (models == null || models.Count == 0 || models.Any(m => m == null)) throw new ArgumentException("Supply at least one Silero model.", nameof(models));
            this.models = models.ToArray();
            this.audioFilters = audioFilters == null ? Array.Empty<IAudioFilter>() : audioFilters.ToArray();
            if (this.audioFilters.Any(f => f == null)) throw new ArgumentException("Audio filters cannot contain null.", nameof(audioFilters));
            gateManager = new TurnEndGateManager(turnEndGates, ReportError, Callbacks);
        }

        protected ISileroVadModel GetModel(string sessionId) => models[(sessionId.GetHashCode() & int.MaxValue) % models.Length];
        protected override RecordingSession CreateSession(string sessionId) => new SileroRecordingSession(sessionId,
            Options.PrerollBufferCount, new SileroVadIterator(GetModel(sessionId), SampleRate, SileroOptions.SpeechProbabilityThreshold));

        protected override void InitializeSessionThreshold(RecordingSession session)
        {
            lock (session.SyncRoot)
                if (!session.AmplitudeThreshold.HasValue)
                    session.AmplitudeThreshold = SileroOptions.VolumeDbThreshold.HasValue ? DbToAmplitude(SileroOptions.VolumeDbThreshold.Value) : (double?)null;
        }

        protected override byte[] PrepareSamples(byte[] samples, string sessionId)
        {
            foreach (var filter in audioFilters)
            {
                samples = filter.Process(samples, sessionId);
                if (samples == null) throw new InvalidOperationException("An audio filter returned null.");
            }
            return samples;
        }

        protected override async UniTask<bool> ProcessSamplesCoreAsync(byte[] samples, RecordingSession recordingSession, CancellationToken token)
        {
            var session = (SileroRecordingSession)recordingSession;
            if (samples.Length == 0) return session.IsRecording;
            if (InvokeExtension(ShouldMute))
            {
                // Python's mute path uses ordinary reset, not the stream's explicit STT cancellation path.
                ResetSessionCore(session);
                lock (session.SyncRoot) { session.PrerollBuffer.Clear(); session.VadBuffer.Clear(); }
                return false;
            }

            var duration = SampleDuration(samples);
            lock (session.SyncRoot)
            {
                session.AppendPreroll(samples);
                session.VadBuffer.AddRange(samples);
            }
            var voiced = await DetectSpeechAsync(samples, session, token);
            if (voiced) await NotifyVoicedAsync(session.SessionId);
            token.ThrowIfCancellationRequested();

            lock (session.SyncRoot)
            {
                if (!session.IsRecording)
                {
                    if (voiced)
                    {
                        session.Reset();
                        session.IsRecording = true;
                        foreach (var frame in session.PrerollBuffer) session.Buffer.AddRange(frame);
                        // Preserve upstream's duplicate onset chunk until a separate compatibility change.
                        session.Buffer.AddRange(samples);
                        session.RecordDuration += duration;
                    }
                    return session.IsRecording;
                }
                session.Buffer.AddRange(samples);
                session.RecordDuration += duration;
                if (voiced)
                {
                    session.ResetTurnEndTiming();
                    session.SilenceDuration = 0;
                    gateManager.ResetSession(session.SessionId);
                }
                else session.SilenceDuration += duration;
            }

            await OnRecordingChunkAsync(session, samples, voiced, duration, token);
            CheckRecordingStarted(session);
            if (session.RecordDuration >= Options.MaxDuration)
            {
                await CompleteRecordingAsync(session, session.RecordDuration, true, token);
                gateManager.ResetSession(session.SessionId);
                lock (session.SyncRoot) session.Reset();
            }
            else if (session.SilenceDuration >= Options.SilenceDurationThreshold)
            {
                MarkSilenceThresholdReached(session);
                var recordedDuration = session.RecordDuration - session.SilenceDuration;
                if (recordedDuration >= Options.MinDuration)
                {
                    if (!await ShouldEndTurnWithGateAsync(session, recordedDuration, token)) return session.IsRecording;
                    await CompleteRecordingAsync(session, recordedDuration, false, token);
                }
                lock (session.SyncRoot) session.Reset();
            }
            return session.IsRecording;
        }

        private async UniTask<bool> DetectSpeechAsync(byte[] currentSamples, SileroRecordingSession session, CancellationToken token)
        {
            var requiredBytes = SileroOptions.ChunkSize * 2;
            float[] input;
            lock (session.SyncRoot)
            {
                if (session.VadBuffer.Count < requiredBytes) return false;
                // Deliberately evaluate only the latest window per input chunk, matching AIAvatarKit.
                var offset = session.VadBuffer.Count - requiredBytes;
                input = new float[SileroOptions.ChunkSize];
                for (var i = 0; i < input.Length; i++)
                    input[i] = (short)(session.VadBuffer[offset + i * 2] | session.VadBuffer[offset + i * 2 + 1] << 8) / 32768.0f;
            }
            var detected = false;
            try
            {
                // The detector's asynchronous operation gate preserves input/reset order.
                // Do not hold a session or model monitor while a browser inference is pending.
                detected = await session.Iterator.PredictAsync(input, SileroOptions.UseVadIterator, token);
                lock (session.SyncRoot)
                {
                    if (SileroOptions.UseVadIterator)
                    {
                        // The upstream iterator reports boundaries; no boundary preserves session activity.
                        if (session.Iterator.SpeechTransition.HasValue) session.IsSpeechActive = session.Iterator.SpeechTransition.Value;
                        detected = session.IsSpeechActive;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { ReportError(ex); }
            lock (session.SyncRoot)
            {
                if (detected && session.AmplitudeThreshold.HasValue)
                    detected = MaxAmplitude(currentSamples) > session.AmplitudeThreshold.Value;
                if (session.VadBuffer.Count > requiredBytes * 2)
                    session.VadBuffer.RemoveRange(0, session.VadBuffer.Count - requiredBytes);
            }
            return detected;
        }

        protected virtual UniTask OnRecordingChunkAsync(SileroRecordingSession session, byte[] samples, bool voiced, double duration, CancellationToken token) => UniTask.CompletedTask;

        protected virtual UniTask CompleteRecordingAsync(SileroRecordingSession session, double recordedDuration, bool atMaximum, CancellationToken token)
        {
            lock (session.SyncRoot)
                PublishSpeechDetected(new SpeechDetectionResult(session.Buffer.ToArray(), null, BuildVadPerformanceMetadata(session), recordedDuration, session.SessionId));
            return UniTask.CompletedTask;
        }

        protected virtual async UniTask<bool> ShouldEndTurnWithGateAsync(SileroRecordingSession session, double recordedDuration, CancellationToken token)
        {
            if (!HasTurnEndGates) return true;
            TurnEndRequest request;
            lock (session.SyncRoot) request = new TurnEndRequest
            {
                Audio = session.Buffer.ToArray(), SampleRate = SampleRate, Channels = Channels,
                RecordedDuration = recordedDuration, SilenceDuration = session.SilenceDuration,
                SessionId = session.SessionId, Text = session.LastRecognizedText, Session = session
            };
            var shouldEnd = await InvokeExtensionAsync(() => gateManager.ShouldEndTurnAsync(request, Options.SilenceDurationThreshold, token));
            lock (session.SyncRoot)
            {
                if (!shouldEnd && gateManager.GetSessionState(session.SessionId).IsActive) session.TurnEndGateHeld = true;
                else if (shouldEnd) FinalizeTurnEndGateTiming(session);
            }
            return shouldEnd;
        }

        protected virtual void MarkSilenceThresholdReached(SileroRecordingSession session)
        {
            lock (session.SyncRoot)
            {
                if (session.SilenceThresholdReachedAt.HasValue) return;
                session.SilenceThresholdReachedAt = Clock.ElapsedSeconds;
                session.SpeechEndAt = Clock.UtcNow.AddSeconds(-session.SilenceDuration);
                session.SilenceThresholdTime = session.SilenceDuration;
                if (HasTurnEndGates) session.TurnEndGateHeld = false;
            }
        }

        private void FinalizeTurnEndGateTiming(SileroRecordingSession session)
        {
            if (HasTurnEndGates && session.SilenceThresholdReachedAt.HasValue && !session.TurnEndGateTime.HasValue)
                session.TurnEndGateTime = Math.Max(0, Clock.ElapsedSeconds - session.SilenceThresholdReachedAt.Value - (session.SttAfterThresholdTime ?? 0));
        }

        protected IReadOnlyDictionary<string, object> BuildVadPerformanceMetadata(SileroRecordingSession session)
        {
            lock (session.SyncRoot)
            {
                FinalizeTurnEndGateTiming(session);
                if (!session.SpeechEndAt.HasValue) return null;
                return new Dictionary<string, object>
                {
                    ["vad_performance"] = new Dictionary<string, object>
                    {
                        ["speech_end_at"] = session.SpeechEndAt.Value,
                        ["silence_threshold_time"] = session.SilenceThresholdTime,
                        ["stt_after_threshold_time"] = session.SttAfterThresholdTime,
                        ["turn_end_gate_time"] = session.TurnEndGateTime,
                        ["turn_end_gate_held"] = session.TurnEndGateHeld
                    }
                };
            }
        }

        protected override void ResetSessionCore(RecordingSession session)
        {
            gateManager.ResetSession(session.SessionId);
            base.ResetSessionCore(session);
        }
        protected override void ResetSessionAudioStateCore(RecordingSession session, bool clearPreroll)
        {
            base.ResetSessionAudioStateCore(session, clearPreroll);
            lock (session.SyncRoot) ((SileroRecordingSession)session).VadBuffer.Clear();
        }
        protected override void OnSessionDeleted(string sessionId)
        {
            gateManager.ResetSession(sessionId);
            foreach (var filter in audioFilters)
                try { InvokeExtension(() => { filter.ResetSession(sessionId); return true; }); }
                catch (Exception ex) { ReportError(ex); }
        }
        public override async UniTask DrainAsync()
        {
            await base.DrainAsync();
            await gateManager.DrainAsync();
        }

        public UniTask SetSpeechProbabilityThresholdAsync(double threshold, CancellationToken cancellationToken = default) => WithOperationAsync(() =>
        {
            if (double.IsNaN(threshold) || threshold < 0 || threshold > 1) throw new ArgumentOutOfRangeException(nameof(threshold));
            SileroOptions.SpeechProbabilityThreshold = threshold;
            foreach (SileroRecordingSession session in SessionsCore)
            {
                lock (session.SyncRoot) { session.Iterator.Threshold = threshold; session.Iterator.ResetStates(); }
            }
            return UniTask.FromResult(true);
        }, cancellationToken);

        protected override void OnOptionsUpdated(SpeechDetectorOptions previous)
        {
            if (((SileroSpeechDetectorOptions)previous).SpeechProbabilityThreshold == SileroOptions.SpeechProbabilityThreshold) return;
            foreach (SileroRecordingSession session in SessionsCore)
                lock (session.SyncRoot)
                {
                    session.Iterator.Threshold = SileroOptions.SpeechProbabilityThreshold;
                    session.Iterator.ResetStates();
                }
        }

        protected override void ValidateRuntimeOptions(SpeechDetectorOptions previous, SpeechDetectorOptions next)
        {
            base.ValidateRuntimeOptions(previous, next);
            if (((SileroSpeechDetectorOptions)previous).UseVadIterator != ((SileroSpeechDetectorOptions)next).UseVadIterator)
                throw new InvalidOperationException("Restart required: recreate the detector to change UseVadIterator.");
        }

        protected override void OnRuntimeOptionsUpdated(SpeechDetectorOptions previous)
        {
            var previousSilero = (SileroSpeechDetectorOptions)previous;
            if (previousSilero.VolumeDbThreshold != SileroOptions.VolumeDbThreshold)
                ReplaceSessionVolumeThresholds(SileroOptions.VolumeDbThreshold);
            if (previousSilero.SpeechProbabilityThreshold != SileroOptions.SpeechProbabilityThreshold)
            {
                // OnOptionsUpdated resets each iterator; its remembered speech flag must match.
                foreach (SileroRecordingSession session in SessionsCore)
                    lock (session.SyncRoot) session.IsSpeechActive = false;
            }
        }

        public UniTask ResetVadStateAsync(string sessionId = null, CancellationToken cancellationToken = default) => WithOperationAsync(() =>
        {
            if (sessionId == null)
            {
                foreach (SileroRecordingSession session in SessionsCore) lock (session.SyncRoot) session.Iterator.ResetStates();
            }
            else if (TryGetSessionCore(sessionId, out var session)) lock (session.SyncRoot) ((SileroRecordingSession)session).Iterator.ResetStates();
            return UniTask.FromResult(true);
        }, cancellationToken);
    }
}

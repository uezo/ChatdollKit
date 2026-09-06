// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
// See SpeechPipeline/README.ja.md for compatibility and lifecycle decisions.
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.VAD
{
    public sealed class StandardSpeechDetectorEngine : SpeechDetectorBase
    {
        private StandardSpeechDetectorOptions StandardOptions => (StandardSpeechDetectorOptions)Options;
        public StandardSpeechDetectorEngine(StandardSpeechDetectorOptions options = null, ISpeechDetectorClock clock = null)
            : base(options ?? new StandardSpeechDetectorOptions(), clock) { }

        protected override RecordingSession CreateSession(string sessionId) => new RecordingSession(sessionId, Options.PrerollBufferCount);
        protected override void InitializeSessionThreshold(RecordingSession session)
        {
            if (!session.AmplitudeThreshold.HasValue || session.AmplitudeThreshold == 0)
                session.AmplitudeThreshold = DbToAmplitude(StandardOptions.VolumeDbThreshold);
        }

        protected override void OnRuntimeOptionsUpdated(SpeechDetectorOptions previous)
        {
            if (((StandardSpeechDetectorOptions)previous).VolumeDbThreshold != StandardOptions.VolumeDbThreshold)
                ReplaceSessionVolumeThresholds(StandardOptions.VolumeDbThreshold);
        }

        protected override async UniTask<bool> ProcessSamplesCoreAsync(byte[] samples, RecordingSession session, CancellationToken token)
        {
            if (InvokeExtension(ShouldMute))
            {
                ResetSessionAudioStateCore(session, true);
                return false;
            }
            var duration = SampleDuration(samples);
            bool voiced;
            lock (session.SyncRoot)
            {
                session.AppendPreroll(samples);
                voiced = MaxAmplitude(samples) > session.AmplitudeThreshold;
            }
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
                        // Intentional upstream compatibility: the onset chunk also appears in pre-roll.
                        session.Buffer.AddRange(samples);
                        session.RecordDuration += duration;
                    }
                    return session.IsRecording;
                }
                session.Buffer.AddRange(samples);
                session.RecordDuration += duration;
                session.SilenceDuration = voiced ? 0 : session.SilenceDuration + duration;
                CheckRecordingStarted(session);
                // Standard checks silence before maximum duration, unlike Silero.
                if (session.SilenceDuration >= Options.SilenceDurationThreshold)
                {
                    var recordedDuration = session.RecordDuration - session.SilenceDuration;
                    if (recordedDuration >= Options.MinDuration)
                        PublishSpeechDetected(new SpeechDetectionResult(session.Buffer.ToArray(), null, null, recordedDuration, session.SessionId));
                    session.Reset();
                }
                else if (session.RecordDuration >= Options.MaxDuration)
                {
                    PublishSpeechDetected(new SpeechDetectionResult(session.Buffer.ToArray(), null, null, session.RecordDuration, session.SessionId));
                    session.Reset();
                }
                return session.IsRecording;
            }
        }
    }
}

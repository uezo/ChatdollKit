using ChatdollKit.SpeechPipeline.VAD;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.VAD.Silero
{
    public class SileroRecordingSession : RecordingSession
    {
        internal readonly List<byte> VadBuffer = new List<byte>();
        internal readonly SileroVadIterator Iterator;
        public bool IsSpeechActive { get; internal set; }
        public DateTimeOffset? SpeechEndAt { get; internal set; }
        public double? SilenceThresholdReachedAt { get; internal set; }
        public double? SilenceThresholdTime { get; internal set; }
        public double? SttAfterThresholdTime { get; internal set; }
        public double? TurnEndGateTime { get; internal set; }
        public bool? TurnEndGateHeld { get; internal set; }

        internal SileroRecordingSession(string sessionId, int prerollBufferCount, SileroVadIterator iterator)
            : base(sessionId, prerollBufferCount) { Iterator = iterator; }

        internal void ResetTurnEndTiming()
        {
            SpeechEndAt = null;
            SilenceThresholdReachedAt = SilenceThresholdTime = SttAfterThresholdTime = TurnEndGateTime = null;
            TurnEndGateHeld = null;
        }

        protected internal override void Reset()
        {
            base.Reset();
            IsSpeechActive = false;
            AmplitudeThreshold = null;
            ResetTurnEndTiming();
            Iterator.ResetStates();
            // Preserve pre-roll and VAD buffering between utterances, matching AIAvatarKit.
        }
    }

    internal sealed class SileroVadIterator
    {
        private readonly ISileroVadModel model;
        internal double Threshold;
        private readonly int sampleRate;
        private bool triggered;
        private long temporaryEnd;
        private long currentSample;
        internal bool? SpeechTransition { get; private set; }
        internal SileroVadIterator(ISileroVadModel model, int sampleRate, double threshold)
        {
            this.model = model;
            this.sampleRate = sampleRate;
            Threshold = threshold;
            ResetStates();
        }
        internal void ResetStates()
        {
            model.ResetStates();
            triggered = false;
            temporaryEnd = currentSample = 0;
            SpeechTransition = null;
        }
        internal async UniTask<bool> PredictAsync(float[] samples, bool useIterator, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpeechTransition = null;
            var probability = await model.PredictAsync(samples, sampleRate, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!useIterator) return probability > Threshold;
            currentSample += samples.Length;
            if (probability >= Threshold)
            {
                temporaryEnd = 0;
                if (!triggered) { triggered = true; SpeechTransition = true; }
            }
            if (probability < Threshold - 0.15 && triggered)
            {
                if (temporaryEnd == 0) temporaryEnd = currentSample;
                if (currentSample - temporaryEnd >= sampleRate * 0.1)
                {
                    temporaryEnd = 0;
                    triggered = false;
                    SpeechTransition = false;
                }
            }
            return triggered;
        }
    }
}

using System;
using ChatdollKit.Orchestration;

namespace ChatdollKit.UI.MessageWindow
{
    /// <summary>Per-window placeholder timing. Never changes recognition or conversation state.</summary>
    internal sealed class UserSpeechPromptState
    {
        private string utteranceId, runId;
        private long generation;
        private double voicedSeconds, silentSeconds, lastObservedAt, lastVoicedReceivedAt;
        private bool hasObservation, ready, expired;

        internal void Reset()
        {
            utteranceId = runId = null;
            generation = 0;
            voicedSeconds = silentSeconds = 0;
            hasObservation = ready = expired = false;
        }

        internal void Receive(ConversationSnapshot snapshot, ConversationEvent item, double now,
            MessageWindowOptions options)
        {
            var utterance = snapshot?.DisplayUserUtterance;
            if (utterance == null || !utterance.IsAwaitingRecognition)
            {
                Reset();
                return;
            }
            if (utterance.Id != utteranceId || snapshot.RunId != runId || snapshot.Generation != generation)
            {
                Reset();
                utteranceId = utterance.Id;
                runId = snapshot.RunId;
                generation = snapshot.Generation;
                lastVoicedReceivedAt = now;
            }
            Expire(now, options);
            if (item == null ||
                (item.Kind != ConversationEventKind.UserSpeechStarted && item.Kind != ConversationEventKind.UserSpeechActivity) ||
                item.RecognitionId != utterance.RecognitionId || !item.IsSpeechActive.HasValue) return;

            var tolerance = NonNegative(options.UserSpeechPromptSilenceTolerance);
            var active = item.IsSpeechActive.Value;
            var observedAt = item.ObservedAtSeconds;
            if (!Finite(observedAt)) return;
            if (item.AudioDurationSeconds.HasValue)
            {
                // Local facts contain the actual PCM duration, including unvoiced chunks.
                var duration = item.AudioDurationSeconds.Value;
                if (!Finite(duration) || duration < 0) return;
                if (active)
                {
                    if (silentSeconds > tolerance + 1e-6) voicedSeconds = 0;
                    silentSeconds = 0;
                    voicedSeconds += duration;
                }
                else
                {
                    silentSeconds += duration;
                    if (silentSeconds > tolerance + 1e-6) voicedSeconds = 0;
                }
            }
            else if (active)
            {
                // A remote positive-only stream has no PCM duration. Estimate continuity from
                // source observation times, not Unity delivery times (events may arrive in a batch).
                var elapsed = observedAt - lastObservedAt;
                if (hasObservation && elapsed >= 0 && elapsed <= tolerance + 1e-6)
                    voicedSeconds += elapsed;
                else voicedSeconds = 0;
            }
            else
            {
                voicedSeconds = 0;
            }
            hasObservation = active || item.AudioDurationSeconds.HasValue;
            lastObservedAt = observedAt;
            if (!active) return;
            expired = false;
            lastVoicedReceivedAt = now;
            if (voicedSeconds + 1e-6 >= NonNegative(options.UserSpeechPromptDelay)) ready = true;
        }

        internal bool CanShow(double now, MessageWindowOptions options)
        {
            Expire(now, options);
            return utteranceId != null && !expired &&
                (ready || NonNegative(options.UserSpeechPromptDelay) == 0);
        }

        private void Expire(double now, MessageWindowOptions options)
        {
            var timeout = NonNegative(options.UserSpeechPromptTimeout);
            if (utteranceId == null || timeout == 0 || now - lastVoicedReceivedAt < timeout) return;
            ready = false;
            expired = true;
            voicedSeconds = silentSeconds = 0;
            hasObservation = false;
        }

        private static double NonNegative(float value) => Finite(value) ? Math.Max(0, value) : 0;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

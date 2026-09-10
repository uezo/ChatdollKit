using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.STT;

namespace ChatdollKit.SpeechPipeline.VAD.Silero
{
    public sealed class SileroStreamRecordingSession : SileroRecordingSession
    {
        internal ISpeechRecognizer SpeechRecognizerOverride;
        internal string RecognitionId;
        internal bool RecognitionClosed, RecognitionNotified;
        internal Action RecognitionReset;
        internal UniTask? PendingRecognitionTask;
        internal CancellationTokenSource PendingRecognitionCancellation;
        // Ordinary utterance resets retain these tasks; an explicit audio reset cancels all of them.
        internal readonly HashSet<CancellationTokenSource> RecognitionCancellations = new HashSet<CancellationTokenSource>();
        public double SegmentDuration { get; internal set; }
        public double SegmentSilenceDuration { get; internal set; }
        public bool SegmentFired { get; internal set; }
        public int RecognitionSequence { get; internal set; }

        internal SileroStreamRecordingSession(
            string sessionId, int prerollBufferCount, SileroVadIterator iterator)
            : base(sessionId, prerollBufferCount, iterator)
        {
        }

        protected internal override void Reset()
        {
            RecognitionReset?.Invoke();
            RecognitionId = Guid.NewGuid().ToString("N");
            RecognitionClosed = RecognitionNotified = false;
            base.Reset();
            SegmentDuration = SegmentSilenceDuration = 0;
            SegmentFired = false;
            // Deliberately retain AIAvatarKit's reset/sequence behavior: ordinary
            // reset forgets the latest task without cancelling existing recognition.
            PendingRecognitionTask = null;
            PendingRecognitionCancellation = null;
            RecognitionSequence = 0;
            // The session's recognizer override survives a normal reset.
        }
    }
}

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.VAD.Silero
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/VAD/Silero Stream Speech Detector")]
    public sealed class SileroStreamSpeechDetector : SileroSpeechDetector
    {
        [Tooltip("Short pauses that trigger partial recognition with the pipeline's selected STT provider.")]
        public float SegmentSilenceThreshold = 0.2f;
        [Tooltip("Log each partial recognition result on the Unity main thread. Each result contains the utterance so far, not just new words.")]
        public bool LogPartialRecognition = true;

        public override SpeechDetectorOptions BuildOptions()
        {
            var options = new SileroStreamSpeechDetectorOptions { SegmentSilenceThreshold = SegmentSilenceThreshold };
            ApplySettings(options);
            return options;
        }

        public override UniTask<SpeechDetectorLease> CreateDetectorAsync(ISpeechRecognizer stt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stt == null) throw new ArgumentNullException(nameof(stt), "SileroStream requires the pipeline's selected STT provider.");
            return base.CreateDetectorAsync(stt, cancellationToken);
        }

        protected override ISpeechDetector CreateDetector(ISileroVadModel model, ISpeechRecognizer stt, SileroSpeechDetectorOptions options)
        {
            // Creation runs on Unity's main thread. Do not make recognition wait for logging.
            var mainContext = SynchronizationContext.Current
                ?? throw new InvalidOperationException("Create the detector on Unity's main thread.");
            var detector = new SileroStreamSpeechDetectorEngine(model, stt, (SileroStreamSpeechDetectorOptions)options);
            detector.SpeechDetecting += (text, session) =>
            {
                var sessionId = session.SessionId;
                mainContext.Post(_ =>
                {
                    // Discard notifications from a stopped/replaced detector or a destroyed component.
                    if (this != null && isActiveAndEnabled && ReferenceEquals(Detector, detector) && LogPartialRecognition)
                        Debug.Log($"[STT partial][{sessionId}] {text}", this);
                }, null);
                return UniTask.CompletedTask;
            };
            return detector;
        }
    }
}

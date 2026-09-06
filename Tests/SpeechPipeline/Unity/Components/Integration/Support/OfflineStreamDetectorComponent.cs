using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Integration
{
    public sealed class OfflineStreamDetectorComponent : SpeechDetectorComponent
    {
        public readonly List<SpeechDetectorLease> Created = new List<SpeechDetectorLease>();
        public readonly List<OfflineVadModel> Models = new List<OfflineVadModel>();
        public SpeechCompletionSource<bool> NextCreationGate;
        public SpeechCompletionSource<bool> CreationEntered;
        public CancellationToken CreationToken;
        public override SpeechDetectorOptions BuildOptions() => new SileroStreamSpeechDetectorOptions();
        public override async UniTask<SpeechDetectorLease> CreateDetectorAsync(ISpeechRecognizer recognizer, CancellationToken cancellationToken)
        {
            var options = (SileroStreamSpeechDetectorOptions)BuildOptions();
            var gate = NextCreationGate; NextCreationGate = null;
            CreationToken = cancellationToken;
            CreationEntered?.TrySetResult(true);
            if (gate != null) await gate.Task; // Intentionally emulate a model load that completes after cancellation.
            var model = new OfflineVadModel(); Models.Add(model);
            var result = new SpeechDetectorLease(new SileroStreamSpeechDetectorEngine(model, recognizer, options), model);
            Created.Add(result);
            return result;
        }
    }
}

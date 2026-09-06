using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.VAD
{
    public sealed class SpeechDetectorLease
    {
        public ISpeechDetector Detector { get; }
        private readonly IDisposable ownedModel;
        private readonly object sync = new object();
        private UniTask? disposal;

        public SpeechDetectorLease(ISpeechDetector detector, IDisposable ownedModel = null)
        {
            Detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.ownedModel = ownedModel;
        }

        public UniTask DisposeAsync()
        {
            lock (sync)
            {
                if (!disposal.HasValue) disposal = SpeechAsync.Share(DisposeCoreAsync());
                return disposal.Value;
            }
        }

        private async UniTask DisposeCoreAsync()
        {
            try { await Detector.DisposeAsync(); }
            finally { ownedModel?.Dispose(); }
        }
    }
}

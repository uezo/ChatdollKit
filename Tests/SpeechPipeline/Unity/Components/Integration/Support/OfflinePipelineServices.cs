using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Integration
{
    public sealed class OfflineRecognizer : SpeechRecognizerBase
    {
        public int Disposals;
        public OfflineRecognizer(SpeechRecognizerOptions options) : base(options) { }
        protected override UniTask<string> TranscribeCoreAsync(byte[] audio, SpeechRecognizerOptions options, CancellationToken token)
            => UniTask.FromResult("offline recognition");
        protected override void DisposeResources() => Disposals++;
    }
    public sealed class OfflineLlm : ILlmService
    {
        public int Disposals, Calls;
        private LlmServiceOptions options;
        public OfflineLlm(LlmServiceOptions options) => this.options = options.Copy();
        public async UniTask<LlmResult> ChatAsync(LlmRequest request, Func<LlmResponse, UniTask> onResponse = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            if (onResponse != null)
            {
                await onResponse(new LlmResponse { ContextId = request.ContextId, Text = "Offline answer.", VoiceText = "Offline answer." });
                await onResponse(new LlmResponse { ContextId = request.ContextId, IsFinal = true });
            }
            return new LlmResult { ContextId = request.ContextId, Text = "Offline answer." };
        }
        public LlmServiceOptions GetOptions() => options.Copy();
        public void UpdateOptions(LlmServiceOptions next) => options = next.Copy();
        public UniTask DisposeAsync() { Disposals++; return UniTask.CompletedTask; }
    }
    public sealed class OfflineSynthesizer : ISpeechSynthesizer
    {
        public int Disposals, Calls;
        private SpeechSynthesizerOptions options;
        public OfflineSynthesizer(SpeechSynthesizerOptions options) => this.options = options.Copy();
        public UniTask<byte[]> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Calls++; return UniTask.FromResult(new byte[] { 0, 0 }); }
        public UniTask<byte[]> GenerateAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
            => SynthesizeAsync(request, cancellationToken);
        public SpeechSynthesizerOptions GetOptions() => options.Copy();
        public void UpdateOptions(SpeechSynthesizerOptions next) => options = next.Copy();
        public UniTask DisposeAsync() { Disposals++; return UniTask.CompletedTask; }
    }
    public sealed class OfflineVadModel : ISileroVadModel
    {
        public int Disposals;
        public UniTask<float> PredictAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(0f);
        }
        public void ResetStates() { }
        public void Dispose() => Disposals++;
    }
    public sealed class OfflineAvatar : IAvatarController
    {
        public int Presentations, Stops;
        public UniTask PresentAsync(AvatarRequest request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Presentations++; return UniTask.CompletedTask; }
        public UniTask StopAsync(CancellationToken cancellationToken = default) { Stops++; return UniTask.CompletedTask; }
    }
}

using ChatdollKit.SpeechPipeline.VAD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;

namespace ChatdollKit.SpeechPipeline
{
    /// <summary>Unity configuration for any ISpeechPipeline, including a future remote implementation.</summary>
    public abstract class SpeechPipelineComponent : LiveSpeechComponent
    {
        public abstract UniTask<SpeechPipelineLease> CreatePipelineAsync(CancellationToken cancellationToken);
    }

    /// <summary>Owns one run and its bindings. The orchestrator using Pipeline must set ownsPipeline=false.
    /// Dispose on the Unity main thread after stopping that orchestrator.</summary>
    public sealed class SpeechPipelineLease
    {
        public ISpeechPipeline Pipeline { get; }
        public int InputSampleRate { get; }
        public int SamplesPerMessage { get; }
        private readonly SpeechDetectorLease vad;
        private readonly LlmConversation conversation;
        private readonly object[] services;
        private readonly List<LiveSpeechComponent> components = new List<LiveSpeechComponent>();
        private UniTask? unbinding, disposal;

        public SpeechPipelineLease(ISpeechPipeline pipeline, int inputSampleRate, int samplesPerMessage,
            SpeechDetectorLease vad = null, LlmConversation conversation = null, params object[] services)
        {
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            if (inputSampleRate <= 0 || samplesPerMessage <= 0) throw new ArgumentOutOfRangeException(nameof(inputSampleRate));
            InputSampleRate = inputSampleRate; SamplesPerMessage = samplesPerMessage;
            this.vad = vad; this.conversation = conversation; this.services = services ?? Array.Empty<object>();
        }
        internal void TrackBinding(LiveSpeechComponent component) => components.Add(component);

        public UniTask StopSettingsUpdatesAsync()
        {
            if (unbinding.HasValue) return unbinding.Value;
            var tasks = new List<UniTask>();
            foreach (var component in components)
            {
                // Managed lifetime remains valid even after Unity has destroyed the component.
                try { tasks.Add(component.UnbindSettingsAsync()); }
                catch (Exception error) { tasks.Add(UniTask.FromException(error)); }
            }
            unbinding = SpeechAsync.Share(SpeechAsync.WhenAll(tasks));
            return unbinding.Value;
        }
        public UniTask DisposeAsync()
        {
            if (disposal.HasValue) return disposal.Value;
            var completion = new SpeechCompletionSource<bool>();
            disposal = completion.Task;
            _ = DisposeCoreAsync(completion);
            return disposal.Value;
        }
        private async UniTask DisposeCoreAsync(SpeechCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            var stopping = new List<UniTask>();
            try { stopping.Add(StopSettingsUpdatesAsync()); } catch (Exception error) { errors.Add(error); }
            // Start BOTH lifetimes before waiting: pipeline Drain may depend on VAD partial recognition.
            try { stopping.Add(Pipeline.DisposeAsync()); } catch (Exception error) { errors.Add(error); }
            if (vad != null) try { stopping.Add(vad.DisposeAsync()); } catch (Exception error) { errors.Add(error); }
            foreach (var task in stopping)
                try { await task; } catch (Exception error) { errors.Add(error); }
            if (conversation != null)
                try { await conversation.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            var disposed = new List<object>();
            foreach (var service in services)
            {
                if (service == null || disposed.Any(item => ReferenceEquals(item, service))) continue;
                disposed.Add(service);
                try { await DisposeServiceAsync(service); } catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count == 0) completion.TrySetResult(true);
            else completion.TrySetException(new AggregateException(errors));
        }
        internal static UniTask DisposeServiceAsync(object service)
        {
            if (service is ILlmService llm) return llm.DisposeAsync();
            if (service is TTS.ISpeechSynthesizer tts) return tts.DisposeAsync();
            if (service is SpeechRecognizerBase stt) return stt.DisposeAsync();
            if (service is IAsyncDisposable asynchronous) return SpeechAsync.FromTask(asynchronous.DisposeAsync().AsTask());
            (service as IDisposable)?.Dispose(); return UniTask.CompletedTask;
        }
    }
}

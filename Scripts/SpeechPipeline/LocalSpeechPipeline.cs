using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.VAD;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline
{
    [DisallowMultipleComponent]
    [AddComponentMenu("ChatdollKit/Speech Pipeline/Local Speech Pipeline")]
    public sealed class LocalSpeechPipeline : SpeechPipelineComponent
    {
        public SpeechDetectorComponent Vad;
        public SpeechRecognizerComponent Stt;
        public LlmServiceComponent Llm;
        public SpeechSynthesizerComponent Tts;
        [Tooltip("Changes to pipeline configuration require Restart Pipeline.")]
        public string SessionId = "default";
        [Min(0.01f)] public float InvokeTimeoutSeconds = 60;
        [Min(1)] public int MaxPendingRequests = 16;
        public Action<SpeechPipelineOptions> ConfigureOptions { get; set; }
        public event Action<SpeechToSpeechPipeline> PipelineCreated;
        public LlmConversation Conversation { get; private set; }
        private readonly List<LiveSpeechComponent> componentBuffer = new List<LiveSpeechComponent>();
        private readonly List<int> selectionBuffer = new List<int>();
        private readonly List<int> lastSelection = new List<int>();
        private int selectionRevision;

        // Also observe component attachment/enabling, which does not invoke this component's OnValidate.
        protected override int ExternalRevision
        {
            get
            {
                GetComponents(componentBuffer);
                selectionBuffer.Clear();
                foreach (var component in componentBuffer)
                    if (component is SpeechDetectorComponent || component is SpeechRecognizerComponent ||
                        component is LlmServiceComponent || component is SpeechSynthesizerComponent) AddSelection(component);
                AddSelection(Vad); AddSelection(Stt); AddSelection(Llm); AddSelection(Tts);
                var changed = selectionBuffer.Count != lastSelection.Count;
                for (var index = 0; !changed && index < selectionBuffer.Count; index++)
                    changed = selectionBuffer[index] != lastSelection[index];
                if (changed)
                {
                    lastSelection.Clear(); lastSelection.AddRange(selectionBuffer); selectionRevision++;
                }
                return selectionRevision;
            }
        }
        private void AddSelection(LiveSpeechComponent component)
        {
            selectionBuffer.Add(component == null ? 0 : component.GetInstanceID());
            selectionBuffer.Add(component != null && component.isActiveAndEnabled ? 1 : 0);
        }

        private T Resolve<T>(T assigned, bool optional = false) where T : MonoBehaviour
        {
            if (assigned != null)
            {
                if (!assigned.isActiveAndEnabled) throw new InvalidOperationException(typeof(T).Name + " must be enabled.");
                return assigned;
            }
            var found = GetComponents<T>().Where(item => item.isActiveAndEnabled).ToArray();
            if (found.Length == 1) return found[0];
            if (found.Length == 0 && optional) return null;
            throw new InvalidOperationException("Assign exactly one " + typeof(T).Name + " in Local Speech Pipeline.");
        }

        public override string GetRestartKey()
        {
            var vad = Resolve(Vad, true); var stt = Resolve(Stt); var llm = Resolve(Llm); var tts = Resolve(Tts);
            return (vad == null ? 0 : vad.GetInstanceID()) + "/" + stt.GetInstanceID() + "/" + llm.GetInstanceID() + "/" +
                tts.GetInstanceID() + "/" + SessionId + "/" + InvokeTimeoutSeconds + "/" + MaxPendingRequests;
        }

        public override async UniTask<SpeechPipelineLease> CreatePipelineAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vadComponent = Resolve(Vad, true); var sttComponent = Resolve(Stt);
            var llmComponent = Resolve(Llm); var ttsComponent = Resolve(Tts);
            var selected = new LiveSpeechComponent[] { this, vadComponent, sttComponent, llmComponent, ttsComponent };
            if (selected.Any(component => component != null && component.IsBound))
                throw new InvalidOperationException("A selected component is already used by a running pipeline.");
            var localStamp = CaptureSettingsStamp();
            var vadStamp = vadComponent?.CaptureSettingsStamp();
            var sttStamp = sttComponent.CaptureSettingsStamp();
            var llmStamp = llmComponent.CaptureSettingsStamp();
            var ttsStamp = ttsComponent.CaptureSettingsStamp();
            var sttOptions = sttComponent.BuildOptions(); sttOptions.Validate();
            var llmOptions = llmComponent.BuildOptions(); llmOptions.Validate();
            var ttsOptions = ttsComponent.BuildOptions(); ttsOptions.Validate();
            var vadOptions = vadComponent?.BuildOptions(); vadOptions?.Validate();
            if (vadOptions != null && (vadOptions.Channels != 1 || vadOptions.SampleRate != sttOptions.SampleRate))
                throw new ArgumentException("VAD and STT must use the same mono PCM sample rate.");
            var options = new SpeechPipelineOptions
            { SessionId = SessionId, InvokeTimeoutSeconds = InvokeTimeoutSeconds, MaxPendingRequests = MaxPendingRequests };
            ConfigureOptions?.Invoke(options); options.Validate();
            ISpeechRecognizer stt = null; ILlmService llm = null; ISpeechSynthesizer tts = null;
            SpeechDetectorLease detector = null; LlmConversation conversation = null; SpeechPipelineLease lease = null;
            try
            {
                stt = sttComponent.CreateRecognizer(sttOptions);
                llm = llmComponent.CreateService(llmOptions);
                tts = ttsComponent.CreateSynthesizer(ttsOptions);
                if (stt == null || llm == null || tts == null) throw new InvalidOperationException("A component returned no service.");
                if (vadComponent != null) detector = await vadComponent.CreateDetectorAsync(stt, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (vadComponent != null && detector == null) throw new InvalidOperationException("VAD component returned no detector.");
                if (this == null || !isActiveAndEnabled || selected.Any(component => !ReferenceEquals(component, null) &&
                    (component == null || !component.isActiveAndEnabled)))
                    throw new OperationCanceledException("A selected component was disabled or destroyed during pipeline creation.", cancellationToken);
                // Another orchestrator may have acquired one of these components while the native model loaded.
                // Check all bindings before publishing ANY runtime reference.
                if (selected.Any(component => component != null && component.IsBound))
                    throw new InvalidOperationException("A selected component was acquired by another pipeline during startup.");
                conversation = llmComponent.HistoryFormat.HasValue
                    ? new LlmConversation(llm, llmComponent.HistoryFormat.Value) : new LlmConversation(llm);
                if (detector != null && (detector.Detector.SampleRate != sttOptions.SampleRate || detector.Detector.Channels != 1))
                    throw new ArgumentException("The created VAD does not match the pipeline audio format.");
                var samples = (vadOptions as SileroSpeechDetectorOptions)?.ChunkSize ?? Math.Max(1, sttOptions.SampleRate / 50);
                var pipeline = new SpeechToSpeechPipeline(stt, conversation, tts, options, detector?.Detector, ownsComponents: false);
                lease = new SpeechPipelineLease(pipeline, sttOptions.SampleRate, samples, detector, conversation, stt, llm, tts);
                Conversation = conversation;
                BindSettingsSnapshot(() => token => UniTask.CompletedTask, localStamp, () => Conversation = null);
                lease.TrackBinding(this);

                sttComponent.Recognizer = stt;
                sttComponent.BindSettingsSnapshot(() =>
                {
                    var next = sttComponent.BuildOptions(sttComponent.ReadOptions(stt)); next.Validate();
                    return token => sttComponent.ApplyRecognizerOptionsAsync(stt, next, token);
                }, sttStamp, () => sttComponent.Recognizer = null);
                lease.TrackBinding(sttComponent);
                llmComponent.Service = llm;
                llmComponent.BindSettingsSnapshot(() =>
                {
                    var next = llmComponent.BuildOptions(conversation.GetOptions()); next.Validate();
                    return token => conversation.UpdateOptionsAsync(next, token);
                }, llmStamp, () => llmComponent.Service = null);
                lease.TrackBinding(llmComponent);
                ttsComponent.Synthesizer = tts;
                ttsComponent.BindSettingsSnapshot(() =>
                {
                    var next = ttsComponent.BuildOptions(tts.GetOptions()); next.Validate();
                    return token => { token.ThrowIfCancellationRequested(); tts.UpdateOptions(next); return UniTask.CompletedTask; };
                }, ttsStamp, () => ttsComponent.Synthesizer = null);
                lease.TrackBinding(ttsComponent);
                if (vadComponent != null)
                {
                    vadComponent.Detector = detector.Detector;
                    vadComponent.BindSettingsSnapshot(() =>
                    {
                        var next = vadComponent.BuildOptions(); next.Validate();
                        return token => vadComponent.ApplyDetectorOptionsAsync(detector.Detector, next, token);
                    }, vadStamp, () => vadComponent.Detector = null);
                    lease.TrackBinding(vadComponent);
                }
                PipelineCreated?.Invoke(pipeline);
                cancellationToken.ThrowIfCancellationRequested();
                return lease;
            }
            catch (Exception failure)
            {
                var errors = new System.Collections.Generic.List<Exception> { failure };
                if (lease != null)
                {
                    try { await lease.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
                }
                else
                {
                    if (detector != null)
                        try { await detector.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
                    if (conversation != null)
                        try { await conversation.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
                    foreach (var service in new object[] { stt, llm, tts })
                    {
                        try { await SpeechPipelineLease.DisposeServiceAsync(service); } catch (Exception error) { errors.Add(error); }
                    }
                }
                if (errors.Count > 1) throw new AggregateException(errors);
                throw;
            }
        }
    }
}

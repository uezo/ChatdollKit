using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.VAD;
using UnityEngine;

namespace ChatdollKit.Orchestration.Samples
{
    /// <summary>Copy this file into your application's scripts and assign Orchestrator.
    /// Call InitializeAsync from your Main after creating its providers and after Unity's Awake phase.</summary>
    public sealed class ChatdollOrchestratorMain : MonoBehaviour
    {
        public ChatdollOrchestrator Orchestrator;

        /// <summary>Providers remain caller-owned. Stop the orchestrator before disposing them.
        /// Pass a VAD to enable microphone input; omit it for text-only requests.</summary>
        public async UniTask InitializeAsync(ISpeechRecognizer stt, ILlmService llm, ISpeechSynthesizer tts,
            ISpeechDetector vad = null, LlmHistoryFormat? historyFormat = null)
        {
            if (Orchestrator == null) throw new InvalidOperationException("Assign the orchestrator.");
            await Orchestrator.StopAsync();
            Orchestrator.AutoStart = false;
            Orchestrator.Options = new ChatdollOrchestratorOptions
            {
                MaxPendingAudioFrames = 100,
                MaxPendingPresentations = 100,
                AllowBargeIn = true
            };
            Orchestrator.InputEnabled = vad != null;
            Orchestrator.BeforeRequestAsync = PrepareRequestAsync;
            Orchestrator.ResponseReceived -= OnResponse;
            Orchestrator.ResponseReceived += OnResponse;

            // Provider-specific configuration belongs to those provider instances.
            // Pipeline callbacks use the same asynchronous runtime as the configured providers.
            var pipelineOptions = new SpeechPipelineOptions
            {
                SessionId = "local-avatar",
                InvokeTimeoutSeconds = 60
            };
            Orchestrator.ConfigureLocalPipeline(stt, llm, tts, pipelineOptions, vad, historyFormat: historyFormat);
            await Orchestrator.StartAsync();
        }

        private UniTask PrepareRequestAsync(SpeechPipelineRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // This orchestrator hook runs on Unity's main thread. Customize the supplied request;
            // start another request or reset/stop the orchestrator after this hook returns.
            request.Channel = gameObject.scene.name;
            return UniTask.CompletedTask;
        }

        public UniTask<SpeechPipelineResponse> SendTextAsync(string text) => Orchestrator.SendTextAsync(text);
        public UniTask StopAsync() => Orchestrator.StopAsync();

        private void OnResponse(SpeechPipelineResponse response)
        {
            // Main-thread notification: update your subtitle/status UI here.
            Debug.Log($"Speech pipeline: {response.Type}");
        }

        private void OnDestroy()
        {
            if (Orchestrator != null) Orchestrator.ResponseReceived -= OnResponse;
            // The orchestrator stops itself when disabled/destroyed. Explicitly await StopAsync
            // before disposing provider instances from your application's shutdown code.
        }
    }
}

using UnityEngine;

namespace ChatdollKit.SpeechPipeline.LLM
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/LLM/OpenAI Responses WebSocket Service")]
    public sealed class OpenAIResponsesWebSocketService : LlmServiceComponent
    {
        public string ApiKey;
        // Retained only so the editor can migrate existing serialized settings.

        public LlmServiceSettings Settings = new LlmServiceSettings();
        public bool EnablePreviousResponseFallback = true;
        [Tooltip("Complete WebSocket endpoint, including /v1/responses.")]
        public string WebSocketUrl = "wss://api.openai.com/v1/responses";
        [Min(1)] public int MaxConnections = 1;
        [Min(0.01f)] public float MaxConnectionAgeSeconds = 3300;

        public override LlmServiceOptions BuildOptions(LlmServiceOptions current = null)
        {
            var options = (OpenAIResponsesWebSocketServiceOptions)(current?.Copy() ?? new OpenAIResponsesWebSocketServiceOptions());
            Settings.ApplyTo(options);
            options.ApiKey = ApiKey;
            options.EnablePreviousResponseFallback = EnablePreviousResponseFallback;
            options.WebSocketUrl = WebSocketUrl;
            options.MaxConnections = MaxConnections;
            options.MaxConnectionAgeSeconds = MaxConnectionAgeSeconds;
            return options;
        }

        public override ILlmService CreateService(LlmServiceOptions options)
            => new OpenAIResponsesWebSocketClient((OpenAIResponsesWebSocketServiceOptions)options);
    }
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public sealed class LlmResponse
    {
        public string ContextId { get; set; }
        public string Text { get; set; }
        public string VoiceText { get; set; }
        public string ResponseId { get; set; }
        public bool IsFinal { get; set; }
        public bool IsRecovery { get; set; }
        public string GuardrailName { get; set; }
        public LlmError Error { get; set; }
        public LlmToolCall ToolCall { get; set; }
        public JObject StructuredContent { get; set; }
    }

    public sealed class LlmResult
    {
        public string ContextId { get; set; }
        public string Text { get; set; }
        public string ResponseId { get; set; }
        public bool RecoveredPreviousResponse { get; set; }
        public LlmError Error { get; set; }
        /// <summary>Current input after request hooks and guards, in the provider's history format.
        /// Null when input was skipped. InitialMessages and prior History are excluded.</summary>
        public JArray InputItems { get; set; }
        /// <summary>Whether ResponseId can continue the complete successful result on the server.</summary>
        public bool CanUsePreviousResponse { get; set; }
        public JArray OutputItems { get; set; }
        public JObject Usage { get; set; }
        public IReadOnlyList<LlmToolCall> ToolCalls { get; set; } = Array.Empty<LlmToolCall>();
    }

    public sealed class LlmProviderResult
    {
        public string ResponseId { get; set; }
        public bool CanUsePreviousResponse { get; set; } = true;
        public bool RecoveredPreviousResponse { get; set; }
        public JArray OutputItems { get; set; }
        public JObject Usage { get; set; }
        public List<LlmToolCall> ToolCalls { get; set; } = new List<LlmToolCall>();
    }

    public sealed class LlmToolCall
    {
        public int Index { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Arguments { get; set; }
        public LlmToolCall Copy() => (LlmToolCall)MemberwiseClone();
    }

    public sealed class LlmError
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public string Parameter { get; set; }
        public string Type { get; set; }
        public int? StatusCode { get; set; }
    }

    public sealed class LlmServiceException : Exception
    {
        public LlmError Error { get; }
        public LlmServiceException(LlmError error) : base(error?.Message ?? "LLM service failed.")
        { Error = error ?? new LlmError { Code = "unknown_error", Message = "LLM service failed." }; }
    }
}

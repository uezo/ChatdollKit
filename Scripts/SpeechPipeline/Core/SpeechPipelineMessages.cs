using System;
using ChatdollKit.SpeechPipeline.LLM;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline
{
    public enum SpeechPipelineResponseType { Accepted, Start, Chunk, ToolCall, Final, Canceled, Error, Stop }

    public sealed class SpeechPipelineRequest
    {
        public string SessionId { get; set; }
        public string ContextId { get; set; }
        public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
        public string UserId { get; set; }
        public string Channel { get; set; }
        public string Text { get; set; }
        /// <summary>Raw signed PCM16 little-endian; format must match the injected recognizer.</summary>
        public byte[] AudioData { get; set; }
        public double AudioDuration { get; set; }
        public string[] ImageUrls { get; set; } = Array.Empty<string>();
        public JObject SystemPromptParameters { get; set; }
        public JObject LlmParameters { get; set; }
        public JObject Metadata { get; set; }
        public bool AllowMerge { get; set; } = true;
        /// <summary>Wait for previously submitted turns. Otherwise a valid, awake request supersedes older work.</summary>
        public bool WaitInQueue { get; set; }
        /// <summary>Presentation/input hint. The pipeline does not know when audio playback finishes.</summary>
        public bool BlockBargeIn { get; set; }
        public bool SkipTts { get; set; }
        public SpeechPipelineRequest Copy()
        {
            var copy = (SpeechPipelineRequest)MemberwiseClone();
            copy.AudioData = (byte[])AudioData?.Clone();
            copy.ImageUrls = ImageUrls == null ? Array.Empty<string>() : (string[])ImageUrls.Clone();
            copy.SystemPromptParameters = (JObject)SystemPromptParameters?.DeepClone();
            copy.LlmParameters = (JObject)LlmParameters?.DeepClone();
            copy.Metadata = (JObject)Metadata?.DeepClone();
            return copy;
        }
    }

    public sealed class SpeechPipelineResponse
    {
        public SpeechPipelineResponseType Type { get; set; }
        public string SessionId { get; set; }
        public string ContextId { get; set; }
        public string TransactionId { get; set; }
        public string UserId { get; set; }
        public string Channel { get; set; }
        public string Text { get; set; }
        public string VoiceText { get; set; }
        public string Language { get; set; }
        /// <summary>Complete TTS audio file bytes, normally WAV. Not transport base64 or necessarily raw PCM.</summary>
        public byte[] AudioData { get; set; }
        public LlmToolCall ToolCall { get; set; }
        public JObject Metadata { get; set; }
        public JObject StructuredContent { get; set; }
        public bool IsTerminal => Type == SpeechPipelineResponseType.Final || Type == SpeechPipelineResponseType.Canceled || Type == SpeechPipelineResponseType.Error;
        public SpeechPipelineResponse Copy()
        {
            var copy = (SpeechPipelineResponse)MemberwiseClone();
            copy.AudioData = (byte[])AudioData?.Clone();
            copy.ToolCall = ToolCall?.Copy();
            copy.Metadata = (JObject)Metadata?.DeepClone();
            copy.StructuredContent = (JObject)StructuredContent?.DeepClone();
            return copy;
        }
    }

    public sealed class SpeechPipelineState
    {
        public string ContextId { get; internal set; }
        public string ActiveTransactionId { get; internal set; }
        public DateTimeOffset? LastConversationUpdatedAt { get; internal set; }
        public int PendingRequests { get; internal set; }
    }
}

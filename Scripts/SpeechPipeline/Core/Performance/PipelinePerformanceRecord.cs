using System;

namespace ChatdollKit.SpeechPipeline.Performance
{
    /// <summary>Timing and correlation data only. Text, audio, credentials, and arbitrary metadata are deliberately absent.
    /// Pipeline durations use the pipeline start as their origin. VAD measurements are recorded separately without adjustment.</summary>
    public sealed class PipelinePerformanceRecord
    {
        public string TransactionId { get; set; }
        public string SessionId { get; set; }
        public string ContextId { get; set; }
        public string UserId { get; set; }
        public string Channel { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public string SttName { get; set; }
        public string LlmName { get; set; }
        public string TtsName { get; set; }
        public string Status { get; set; }
        public string ErrorCode { get; set; }
        public double? SttTime { get; set; }
        public double? StopResponseTime { get; set; }
        public double? BeforeLlmTime { get; set; }
        public double? LlmFirstChunkTime { get; set; }
        public double? LlmFirstVoiceChunkTime { get; set; }
        public double? LlmTime { get; set; }
        public double? TtsFirstChunkTime { get; set; }
        public double? TtsTime { get; set; }
        public double TotalTime { get; set; }
        public double VoiceLength { get; set; }
        public DateTimeOffset? SpeechEndAt { get; set; }
        public double? SilenceThresholdTime { get; set; }
        public double? SttAfterThresholdTime { get; set; }
        public double? TurnEndGateTime { get; set; }
        public bool? TurnEndGateHeld { get; set; }

        // All properties are immutable strings or value types, so copying the instance fully isolates mutable state.
        public PipelinePerformanceRecord Copy() => (PipelinePerformanceRecord)MemberwiseClone();
    }
}

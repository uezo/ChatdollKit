using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.LLM;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline
{
    public interface ISpeechPipelineClock
    {
        DateTimeOffset UtcNow { get; }
        double ElapsedSeconds { get; }
    }
    public sealed class SpeechPipelineClock : ISpeechPipelineClock
    {
        private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public double ElapsedSeconds => watch.Elapsed.TotalSeconds;
    }

    public sealed class SpeechPipelineOptions
    {
        public string SessionId { get; set; } = "default";
        public string ContextId { get; set; }
        public double InvokeTimeoutSeconds { get; set; } = 60;
        public int MaxPendingRequests { get; set; } = 16;
        public string[] Wakewords { get; set; } = Array.Empty<string>();
        public double WakewordTimeoutSeconds { get; set; } = 60;
        public double MergeRequestThresholdSeconds { get; set; }
        public string MergeRequestPrefix { get; set; } = "$Previous user's request and your response have been canceled. Please respond again to the following request:\n\n";
        public double TimestampIntervalSeconds { get; set; }
        public string TimestampPrefix { get; set; } = "$Current date and time: ";
        public TimeZoneInfo TimestampTimeZone { get; set; } = TimeZoneInfo.Utc;
        public Func<SpeechPipelineRequest, CancellationToken, UniTask<string>> ValidateRequestAsync { get; set; }
        public Func<SpeechPipelineRequest, CancellationToken, UniTask> OnAcceptedAsync { get; set; }
        public Func<SpeechPipelineRequest, CancellationToken, UniTask> BeforeLlmAsync { get; set; }
        public Func<SpeechPipelineRequest, CancellationToken, UniTask> BeforeTtsAsync { get; set; }
        public Func<SpeechPipelineRequest, SpeechPipelineResponse, CancellationToken, UniTask> OnFinishAsync { get; set; }
        public Func<SpeechPipelineRequest, LlmResponse, CancellationToken, UniTask<JObject>> ProcessLlmChunkAsync { get; set; }
        public SpeechPipelineOptions Copy()
        {
            var copy = (SpeechPipelineOptions)MemberwiseClone();
            copy.Wakewords = Wakewords == null ? Array.Empty<string>() : (string[])Wakewords.Clone();
            return copy;
        }
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SessionId)) throw new ArgumentException("SessionId is required.");
            if (ContextId != null && string.IsNullOrWhiteSpace(ContextId)) throw new ArgumentException("ContextId must be nonempty when supplied.");
            if (MaxPendingRequests < 1) throw new ArgumentOutOfRangeException(nameof(MaxPendingRequests));
            Duration(InvokeTimeoutSeconds, false); Duration(WakewordTimeoutSeconds, true);
            Duration(MergeRequestThresholdSeconds, true); Duration(TimestampIntervalSeconds, true);
            if (MergeRequestPrefix == null || TimestampPrefix == null || TimestampTimeZone == null) throw new ArgumentException("Prefixes and timezone must be nonnull.");
            foreach (var word in Wakewords ?? Array.Empty<string>())
                if (string.IsNullOrEmpty(word)) throw new ArgumentException("Wakewords must be nonempty.");
        }
        private static void Duration(double seconds, bool allowZero)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || (!allowZero && seconds == 0) || seconds > int.MaxValue / 1000.0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
        }
    }
}

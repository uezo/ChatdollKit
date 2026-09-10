using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.VAD
{
    /// <summary>Consumes raw interleaved PCM16 little-endian audio. Operations are serialized.
    /// Finalize discards unfinished audio, as in AIAvatarKit; it does not flush an utterance.</summary>
    public interface ISpeechDetector
    {
        int SampleRate { get; }
        int Channels { get; }
        event Func<SpeechDetectionResult, UniTask> SpeechDetected;
        event Func<string, UniTask> RecordingStarted;
        event Func<string, UniTask> Voiced;
        event Action<Exception> Error;
        Func<bool> ShouldMute { get; set; }
        SpeechDetectorOptions GetOptions();
        UniTask UpdateOptionsAsync(SpeechDetectorOptions options, CancellationToken cancellationToken = default);
        UniTask<bool> ProcessSamplesAsync(byte[] samples, string sessionId = "default", CancellationToken cancellationToken = default);
        UniTask ProcessStreamAsync(IAsyncEnumerable<byte[]> stream, string sessionId = "default", CancellationToken cancellationToken = default);
        UniTask ResetSessionAsync(string sessionId = "default", CancellationToken cancellationToken = default);
        UniTask ResetSessionAudioStateAsync(string sessionId = "default", bool clearPreroll = true, CancellationToken cancellationToken = default);
        UniTask FinalizeSessionAsync(string sessionId = "default", CancellationToken cancellationToken = default);
        UniTask<bool> IsRecordingAsync(string sessionId = "default", CancellationToken cancellationToken = default);
        UniTask<object> GetSessionDataAsync(string sessionId, string key, CancellationToken cancellationToken = default);
        UniTask SetSessionDataAsync(string sessionId, string key, object value, bool createSession = false, CancellationToken cancellationToken = default);
        UniTask SetVolumeDbThresholdAsync(string sessionId, double value, CancellationToken cancellationToken = default);
        UniTask DrainAsync();
        UniTask DisposeAsync();
    }

    public sealed class SpeechDetectionResult
    {
        public byte[] Audio { get; }
        public string Text { get; }
        public IReadOnlyDictionary<string, object> Metadata { get; }
        /// <summary>Excludes pre-roll and, on silence completion, trailing silence. Audio includes both.</summary>
        public double RecordedDuration { get; }
        public string SessionId { get; }
        public string RecognitionId { get; }
        public SpeechDetectionResult(byte[] audio, string text, IReadOnlyDictionary<string, object> metadata, double recordedDuration, string sessionId, string recognitionId = null)
        {
            Audio = audio;
            Text = text;
            Metadata = metadata;
            RecordedDuration = recordedDuration;
            SessionId = sessionId;
            RecognitionId = recognitionId;
        }
    }


    public interface ISpeechDetectorClock
    {
        DateTimeOffset UtcNow { get; }
        double ElapsedSeconds { get; }
    }

    public sealed class SpeechDetectorClock : ISpeechDetectorClock
    {
        private readonly System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public double ElapsedSeconds => stopwatch.Elapsed.TotalSeconds;
    }
}

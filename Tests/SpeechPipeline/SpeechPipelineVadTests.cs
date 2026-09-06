using ChatdollKit.SpeechPipeline.Remote;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.VAD;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class SpeechPipelineVadTests
    {
        private const string Session = "vad-integration";

        [Test]
        public async NUnitTask StandardPcmProducesOneTurnWithDetectorAudioAndDuration()
        {
            var vad = Standard();
            byte[] recognizedAudio = null;
            string recognizedSession = null;
            SpeechPipelineRequest mapped = null;
            var stt = new DummySpeechRecognizer(handler: (session, audio, token) =>
            {
                recognizedSession = session; recognizedAudio = (byte[])audio.Clone();
                return UniTask.FromResult(new SpeechRecognitionResult { Text = "heard speech" });
            });
            var options = Options();
            options.BeforeLlmAsync = async (request, token) =>
            {
                mapped = request.Copy();
                Assert.That(await vad.IsRecordingAsync(Session, token), Is.False,
                    "Turn execution must not inherit VAD's callback reentry guard.");
            };
            var llm = new FakeLlm();
            var tts = Tts();
            var pipeline = Pipeline(vad, stt, llm, tts, options);
            var responses = Observe(pipeline);
            var onset = Pcm(1000, 3200);
            var tail = Pcm(0, 1600);
            try
            {
                await pipeline.ProcessAudioSamplesAsync(onset);
                await pipeline.ProcessAudioSamplesAsync(tail);
                await Complete(pipeline.DrainAsync());
                SpeechAsyncAssert.NoPendingOperations(pipeline, "audioTasks");
                SpeechAsyncAssert.NoPendingOperations(pipeline, "drainTasks");
                Assert.That(recognizedSession, Is.EqualTo(Session));
                CollectionAssert.AreEqual(onset.Concat(onset).Concat(tail).ToArray(), recognizedAudio);
                Assert.That(mapped.AudioDuration, Is.EqualTo(0.2).Within(1e-9));
                Assert.That(mapped.Text, Is.EqualTo("heard speech"));
                Assert.That(llm.Requests.Single().Text, Is.EqualTo("heard speech"));
                Assert.That(responses.Count(item => item.Type == SpeechPipelineResponseType.Final), Is.EqualTo(1));
                Assert.That(responses.Single(item => item.Type == SpeechPipelineResponseType.Chunk).AudioData, Is.EqualTo(new byte[] { 1, 2, 3 }));
            }
            finally { await pipeline.DisposeAsync(); await vad.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask AudioProcessingReturnsWithoutAwaitingRecognitionButDrainIncludesTurn()
        {
            var entered = Signal();
            var release = Signal();
            var vad = Standard();
            var stt = new DummySpeechRecognizer(handler: async (session, audio, token) =>
            {
                entered.TrySetResult(true);
                await release.Task;
                token.ThrowIfCancellationRequested();
                return new SpeechRecognitionResult { Text = "heard speech" };
            });
            var tts = Tts();
            var pipeline = Pipeline(vad, stt, new FakeLlm(), tts);
            var responses = Observe(pipeline);
            try
            {
                await Complete(pipeline.ProcessAudioSamplesAsync(Pcm(1000, 3200)));
                await Complete(pipeline.ProcessAudioSamplesAsync(Pcm(0, 1600)));
                await Complete(entered.Task);
                var drain = pipeline.DrainAsync();
                Assert.That(drain.Status.IsCompleted(), Is.False);
                release.TrySetResult(true);
                await Complete(drain);
                Assert.That(responses.Count(item => item.Type == SpeechPipelineResponseType.Final), Is.EqualTo(1));
            }
            finally { release.TrySetResult(true); await pipeline.DisposeAsync(); await vad.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ResetDiscardsRecordingAndPrerollBeforeNextUtterance()
        {
            var vad = Standard();
            var recognized = new ConcurrentQueue<byte[]>();
            var stt = new DummySpeechRecognizer(handler: (session, audio, token) =>
            { recognized.Enqueue((byte[])audio.Clone()); return UniTask.FromResult(new SpeechRecognitionResult { Text = "new speech" }); });
            var tts = Tts();
            var pipeline = Pipeline(vad, stt, new FakeLlm(), tts);
            try
            {
                await pipeline.ProcessAudioSamplesAsync(Pcm(1000, 3200));
                await Complete(pipeline.ResetAsync());
                Assert.That(await vad.IsRecordingAsync(Session), Is.False);
                await pipeline.ProcessAudioSamplesAsync(Pcm(0, 1600));
                await Complete(pipeline.DrainAsync());
                Assert.That(recognized, Is.Empty);
                var onset = Pcm(2000, 3200);
                var tail = Pcm(0, 1600);
                await pipeline.ProcessAudioSamplesAsync(onset);
                await pipeline.ProcessAudioSamplesAsync(tail);
                await Complete(pipeline.DrainAsync());
                CollectionAssert.AreEqual(onset.Concat(onset).Concat(tail).ToArray(), recognized.Single());
            }
            finally { await pipeline.DisposeAsync(); await vad.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ResetCancelsRecognitionAndAllowsACompleteNewTurn()
        {
            var entered = Signal();
            var canceled = Signal();
            var count = 0;
            var vad = Standard();
            var stt = new DummySpeechRecognizer(handler: async (session, audio, token) =>
            {
                if (Interlocked.Increment(ref count) == 1)
                {
                    entered.TrySetResult(true);
                    try { await UniTask.Never(token); }
                    finally { canceled.TrySetResult(true); }
                }
                return new SpeechRecognitionResult { Text = "new turn" };
            });
            var llm = new FakeLlm();
            var tts = Tts();
            var pipeline = Pipeline(vad, stt, llm, tts);
            var responses = Observe(pipeline);
            try
            {
                await pipeline.ProcessAudioSamplesAsync(Pcm(1000, 3200));
                await pipeline.ProcessAudioSamplesAsync(Pcm(0, 1600));
                await Complete(entered.Task);
                await Complete(pipeline.ResetAsync("fresh-context"));
                Assert.That(canceled.Task.Status.IsCompleted(), Is.True);
                Assert.That(responses.Any(item => item.Type == SpeechPipelineResponseType.Chunk), Is.False);
                var responsesAfterReset = responses.Count;
                await Complete(pipeline.DrainAsync());
                Assert.That(responses.Count, Is.EqualTo(responsesAfterReset), "Old work cannot publish after reset returns.");
                await pipeline.ProcessAudioSamplesAsync(Pcm(2000, 3200));
                await pipeline.ProcessAudioSamplesAsync(Pcm(0, 1600));
                await Complete(pipeline.DrainAsync());
                Assert.That(llm.Requests.Single().ContextId, Is.EqualTo("fresh-context"));
                Assert.That(responses.Count(item => item.Type == SpeechPipelineResponseType.Final), Is.EqualTo(1));
            }
            finally { await pipeline.DisposeAsync(); await vad.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask AlreadyRecognizedVadTextSkipsBatchSttAndCopiesMetadata()
        {
            var vad = new ManualVad();
            var sttCalls = 0;
            var stt = new DummySpeechRecognizer(handler: (session, audio, token) =>
            { Interlocked.Increment(ref sttCalls); throw new InvalidOperationException("Already recognized text must skip STT."); });
            SpeechPipelineRequest mapped = null;
            var entered = Signal();
            var release = Signal();
            var options = Options();
            options.OnAcceptedAsync = async (request, token) => { entered.TrySetResult(true); await release.Task; };
            options.BeforeLlmAsync = (request, token) => { mapped = request.Copy(); return UniTask.CompletedTask; };
            var tts = Tts();
            var pipeline = Pipeline(vad, stt, new FakeLlm(), tts, options);
            var metadata = new Dictionary<string, object> { ["segment_count"] = 2, ["vad_performance"] = new Dictionary<string, object> { ["duration"] = 0.75 } };
            var audio = Pcm(1000, 8);
            try
            {
                await Complete(vad.Emit(new SpeechDetectionResult(audio, "streamed speech", metadata, 0.75, Session)));
                await Complete(entered.Task);
                Array.Clear(audio, 0, audio.Length);
                metadata["segment_count"] = 999;
                ((Dictionary<string, object>)metadata["vad_performance"])["duration"] = 999;
                release.TrySetResult(true);
                await Complete(pipeline.DrainAsync());
                Assert.That(mapped.Text, Is.EqualTo("streamed speech"));
                Assert.That(mapped.AudioDuration, Is.EqualTo(0.75));
                Assert.That((int)mapped.Metadata["segment_count"], Is.EqualTo(2));
                Assert.That((double)mapped.Metadata["vad_performance"]["duration"], Is.EqualTo(0.75));
                Assert.That(mapped.AudioData.Any(value => value != 0), Is.True);
                Assert.That(sttCalls, Is.Zero);
                Assert.That(vad.SessionDataAccesses, Is.Zero, "The VAD callback must not reenter session control APIs.");
            }
            finally { release.TrySetResult(true); await pipeline.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ForeignVadSessionIsIgnored()
        {
            var vad = new ManualVad();
            var llm = new FakeLlm();
            var tts = Tts();
            var pipeline = Pipeline(vad, new DummySpeechRecognizer("heard"), llm, tts);
            var responses = Observe(pipeline);
            try
            {
                await vad.Emit(new SpeechDetectionResult(Pcm(1000, 8), "foreign speech", null, 0.2, "another-session"));
                await Complete(pipeline.DrainAsync());
                Assert.That(llm.Requests, Is.Empty);
                Assert.That(responses, Is.Empty);
            }
            finally { await pipeline.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DisposeUnsubscribesWithoutClosingCallerOwnedDetector()
        {
            var vad = new ManualVad();
            var llm = new FakeLlm();
            var tts = Tts();
            var pipeline = Pipeline(vad, new DummySpeechRecognizer("heard"), llm, tts);
            var responses = Observe(pipeline);
            var errors = new ConcurrentQueue<Exception>();
            pipeline.Error += errors.Enqueue;
            try
            {
                Assert.That(vad.SpeechSubscribers, Is.EqualTo(1));
                await Complete(pipeline.DisposeAsync());
                Assert.That(vad.SpeechSubscribers, Is.Zero);
                Assert.That(vad.DisposeCount, Is.Zero);
                var responseCount = responses.Count;
                await vad.Emit(new SpeechDetectionResult(Pcm(1000, 8), "late speech", null, 0.2, Session));
                vad.Report(new InvalidOperationException("late detector error"));
                Assert.That(llm.Requests, Is.Empty);
                Assert.That(responses.Count, Is.EqualTo(responseCount));
                Assert.That(errors, Is.Empty);
                await SpeechAsyncAssert.ThrowsAsync<ObjectDisposedException>(async () => await pipeline.ProcessAudioSamplesAsync(Pcm(1000, 8)));
            }
            finally { await pipeline.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask OwnedDetectorClosesExactlyOnce()
        {
            var vad = new ManualVad();
            var llm = new FakeLlm();
            var tts = Tts();
            var pipeline = new SpeechToSpeechPipeline(new DummySpeechRecognizer("heard"), llm, tts,
                Options(), vad: vad, ownsComponents: true, historyFormat: LlmHistoryFormat.ChatCompletions);
            await Complete(pipeline.DisposeAsync());
            await pipeline.DisposeAsync();
            Assert.That(vad.DisposeCount, Is.EqualTo(1));
            Assert.That(llm.DisposeCount, Is.EqualTo(1));
            Assert.That(vad.SpeechSubscribers, Is.Zero);
        }

        [Test]
        public async NUnitTask DisposeCancelsAndDrainsAudioInputInProgress()
        {
            var entered = Signal();
            var vad = new ManualVad();
            var ended = false;
            vad.ProcessHandler = async (samples, session, token) =>
            {
                Assert.That(session, Is.EqualTo(Session));
                entered.TrySetResult(true);
                try { await UniTask.Never(token); }
                finally { ended = true; }
                return false;
            };
            var tts = Tts();
            var pipeline = Pipeline(vad, new DummySpeechRecognizer("heard"), new FakeLlm(), tts);
            try
            {
                var pending = pipeline.ProcessAudioSamplesAsync(Pcm(1000, 3200));
                await Complete(entered.Task);
                await Complete(pipeline.DisposeAsync());
                Assert.That(ended, Is.True);
                Assert.That(pending.Status.IsCompleted(), Is.True);
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await pending);
                Assert.That(vad.DisposeCount, Is.Zero);
            }
            finally { await pipeline.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DisposeStartsOwnedDetectorShutdownAndWaitsForItsCompletion()
        {
            var entered = Signal();
            var closeStarted = Signal();
            var closeRelease = Signal();
            var vad = new ManualVad();
            vad.DrainHandler = async () => { entered.TrySetResult(true); await closeStarted.Task; };
            vad.DisposeHandler = async () => { closeStarted.TrySetResult(true); await closeRelease.Task; };
            var llm = new FakeLlm();
            var pipeline = new SpeechToSpeechPipeline(new DummySpeechRecognizer("heard"), llm, Tts(),
                Options(), vad: vad, ownsComponents: true, historyFormat: LlmHistoryFormat.ChatCompletions);
            try
            {
                var drain = pipeline.DrainAsync();
                await Complete(entered.Task);
                var disposal = pipeline.DisposeAsync();
                await Complete(closeStarted.Task);
                Assert.That(disposal.Status.IsCompleted(), Is.False);
                Assert.That(vad.DisposeCount, Is.EqualTo(1));
                Assert.That(llm.DisposeCount, Is.Zero, "Downstream components must stay alive until owned VAD shutdown completes.");
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await drain);
                closeRelease.TrySetResult(true);
                await Complete(disposal);
                Assert.That(vad.DisposeCount, Is.EqualTo(1));
                Assert.That(llm.DisposeCount, Is.EqualTo(1));
            }
            finally { closeStarted.TrySetResult(true); closeRelease.TrySetResult(true); await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DisposeCompletesWhenDetectorDrainNeedsShutdownToBegin()
        {
            var entered = Signal();
            var closeStarted = Signal();
            var vad = new ManualVad();
            vad.DrainHandler = async () => { entered.TrySetResult(true); await closeStarted.Task; };
            vad.DisposeHandler = () => { closeStarted.TrySetResult(true); return UniTask.CompletedTask; };
            var pipeline = new SpeechToSpeechPipeline(new DummySpeechRecognizer("heard"), new FakeLlm(), Tts(),
                Options(), vad: vad, ownsComponents: true, historyFormat: LlmHistoryFormat.ChatCompletions);
            try
            {
                var drain = pipeline.DrainAsync();
                await Complete(entered.Task);
                await Complete(pipeline.DisposeAsync());
                Assert.That(closeStarted.Task.Status.IsCompleted(), Is.True);
                Assert.That(vad.DisposeCount, Is.EqualTo(1));
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await drain);
            }
            finally { closeStarted.TrySetResult(true); await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DetectorDrainCannotReenterPipelineDisposal()
        {
            var vad = new ManualVad();
            var tts = Tts();
            var pipeline = Pipeline(vad, new DummySpeechRecognizer("heard"), new FakeLlm(), tts);
            vad.DrainHandler = () => pipeline.DisposeAsync();
            try
            {
                var drain = pipeline.DrainAsync();
                Assert.That(drain.Status.IsCompleted(), Is.True, "Synchronous reentry must fail immediately, without waiting on itself.");
                await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await drain);
                Assert.That(vad.SpeechSubscribers, Is.EqualTo(1), "Rejected reentry must not begin disposal.");
            }
            finally { vad.DrainHandler = null; await pipeline.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask PcmFrameValidationAndCanceledInputDoNotProduceATurn()
        {
            var vad = Standard();
            var llm = new FakeLlm();
            var tts = Tts();
            var pipeline = Pipeline(vad, new DummySpeechRecognizer("heard"), llm, tts);
            try
            {
                await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await pipeline.ProcessAudioSamplesAsync(new byte[] { 1 }));
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await pipeline.ProcessAudioSamplesAsync(Pcm(1000, 3200), cancellation.Token));
                }
                await pipeline.ProcessAudioSamplesAsync(Pcm(0, 1600));
                await Complete(pipeline.DrainAsync());
                Assert.That(llm.Requests, Is.Empty);
            }
            finally { await pipeline.DisposeAsync(); await vad.DisposeAsync(); await tts.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask AudioInputRequiresADetector()
        {
            var tts = Tts();
            var pipeline = Pipeline(null, new DummySpeechRecognizer("heard"), new FakeLlm(), tts);
            try { await SpeechAsyncAssert.ThrowsInstanceOfAsync<InvalidOperationException>(async () => await pipeline.ProcessAudioSamplesAsync(Pcm(1000, 8))); }
            finally { await pipeline.DisposeAsync(); await tts.DisposeAsync(); }
        }

        private static SpeechPipelineOptions Options() => new SpeechPipelineOptions { SessionId = Session };
        private static StandardSpeechDetectorEngine Standard() => new StandardSpeechDetectorEngine(new StandardSpeechDetectorOptions
        { SampleRate = 16000, Channels = 1, MinDuration = 0.1, SilenceDurationThreshold = 0.1, PrerollBufferCount = 1 });
        private static ISpeechSynthesizer Tts() => SpeechSynthesizer.Create((request, token) => UniTask.FromResult(new byte[] { 1, 2, 3 }));
        private static SpeechToSpeechPipeline Pipeline(ISpeechDetector vad, ISpeechRecognizer stt, ILlmService llm,
            ISpeechSynthesizer tts, SpeechPipelineOptions options = null) => new SpeechToSpeechPipeline(stt, llm, tts,
                options ?? Options(), vad: vad, historyFormat: LlmHistoryFormat.ChatCompletions);
        private static ConcurrentQueue<SpeechPipelineResponse> Observe(ISpeechPipeline pipeline)
        {
            var responses = new ConcurrentQueue<SpeechPipelineResponse>();
            pipeline.ResponseReceived += response => { responses.Enqueue(response.Copy()); return UniTask.CompletedTask; };
            return responses;
        }
        private static SpeechCompletionSource<bool> Signal() => new SpeechCompletionSource<bool>();
        private static async UniTask Complete(UniTask task)
        {
            task = SpeechAsync.Share(task);
            using (var timeout = new CancellationTokenSource())
            {
                var deadline = SpeechAsync.Delay(TimeSpan.FromSeconds(5), timeout.Token);
                Assert.That(await UniTask.WhenAny(task, deadline), Is.EqualTo(0), "Pipeline/VAD integration must not deadlock.");
                timeout.Cancel();
                await task;
            }
        }
        private static byte[] Pcm(short amplitude, int frames)
        {
            var result = new byte[frames * 2];
            for (var i = 0; i < frames; i++) { result[i * 2] = (byte)amplitude; result[i * 2 + 1] = (byte)(amplitude >> 8); }
            return result;
        }
        private sealed class FakeLlm : ILlmService
        {
            public readonly ConcurrentQueue<LlmRequest> Requests = new ConcurrentQueue<LlmRequest>();
            public int DisposeCount;
            public async UniTask<LlmResult> ChatAsync(LlmRequest request, Func<LlmResponse, UniTask> onResponse = null, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Enqueue(request.Copy());
                if (onResponse != null)
                {
                    await onResponse(new LlmResponse { ContextId = request.ContextId, Text = "reply", VoiceText = "reply" });
                    await onResponse(new LlmResponse { ContextId = request.ContextId, IsFinal = true, Text = "reply" });
                }
                return new LlmResult { ContextId = request.ContextId, Text = "reply", InputItems = new JArray(new JObject { ["role"] = "user", ["content"] = request.Text }),
                    OutputItems = new JArray(new JObject { ["role"] = "assistant", ["content"] = "reply" }) };
            }
            public LlmServiceOptions GetOptions() => new LlmServiceOptions { ApiKey = "test" };
            public void UpdateOptions(LlmServiceOptions options) { }
            public UniTask DisposeAsync() { DisposeCount++; return UniTask.CompletedTask; }
        }
        private sealed class ManualVad : ISpeechDetector
        {
            public int SampleRate => 16000;
            public int Channels => 1;
            public event Func<SpeechDetectionResult, UniTask> SpeechDetected;
            public event Func<string, UniTask> RecordingStarted { add { } remove { } }
            public event Func<string, UniTask> Voiced { add { } remove { } }
            public event Action<Exception> Error;
            public Func<bool> ShouldMute { get; set; }
            public int SpeechSubscribers => SpeechDetected?.GetInvocationList().Length ?? 0;
            public int DisposeCount;
            public int SessionDataAccesses;
            public Func<byte[], string, CancellationToken, UniTask<bool>> ProcessHandler;
            public Func<UniTask> DrainHandler;
            public Func<UniTask> DisposeHandler;
            public async UniTask Emit(SpeechDetectionResult result)
            {
                var handlers = SpeechDetected;
                if (handlers != null) foreach (Func<SpeechDetectionResult, UniTask> handler in handlers.GetInvocationList()) await handler(result);
            }
            public void Report(Exception error) => Error?.Invoke(error);
            public SpeechDetectorOptions GetOptions() => new SpeechDetectorOptions();
            public UniTask UpdateOptionsAsync(SpeechDetectorOptions options, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask<bool> ProcessSamplesAsync(byte[] samples, string sessionId = "default", CancellationToken cancellationToken = default)
                => ProcessHandler == null ? UniTask.FromResult(false) : ProcessHandler(samples, sessionId, cancellationToken);
            public UniTask ProcessStreamAsync(IAsyncEnumerable<byte[]> stream, string sessionId = "default", CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public UniTask ResetSessionAsync(string sessionId = "default", CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask ResetSessionAudioStateAsync(string sessionId = "default", bool clearPreroll = true, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask FinalizeSessionAsync(string sessionId = "default", CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask<bool> IsRecordingAsync(string sessionId = "default", CancellationToken cancellationToken = default) => UniTask.FromResult(false);
            public UniTask<object> GetSessionDataAsync(string sessionId, string key, CancellationToken cancellationToken = default) { SessionDataAccesses++; throw new InvalidOperationException("Unexpected VAD session access."); }
            public UniTask SetSessionDataAsync(string sessionId, string key, object value, bool createSession = false, CancellationToken cancellationToken = default) { SessionDataAccesses++; throw new InvalidOperationException("Unexpected VAD session access."); }
            public UniTask SetVolumeDbThresholdAsync(string sessionId, double value, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask DrainAsync() => DrainHandler == null ? UniTask.CompletedTask : DrainHandler();
            public UniTask DisposeAsync() { DisposeCount++; return DisposeHandler == null ? UniTask.CompletedTask : DisposeHandler(); }
        }
    }
}

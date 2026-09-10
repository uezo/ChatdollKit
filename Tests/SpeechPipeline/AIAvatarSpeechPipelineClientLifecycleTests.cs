using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.Remote;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.Tests.SpeechPipeline
{
#if !UNITY_5_3_OR_NEWER
    [NonParallelizable]
#endif
    public class AIAvatarSpeechPipelineClientLifecycleTests
    {
        private sealed class FakeClock : ISpeechPipelineClock
        {
            public DateTimeOffset UtcNow => new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero).AddSeconds(ElapsedSeconds);
            public double ElapsedSeconds { get; private set; }
            internal void Advance(double seconds) { ElapsedSeconds += seconds; }
        }

        private sealed class DelayObserverScheduler : ISpeechAsyncScheduler, IDisposable
        {
            private readonly ISpeechAsyncScheduler previous = SpeechAsync.Scheduler;
            private int delayCount;
            internal DelayObserverScheduler() { SpeechAsync.Scheduler = this; }
            internal int DelayCount => Volatile.Read(ref delayCount);
            public void Post(Action continuation) => previous.Post(continuation);
            public UniTask DelayAsync(TimeSpan delay, CancellationToken token)
            {
                Interlocked.Increment(ref delayCount);
                return previous.DelayAsync(delay, token);
            }
            public void Dispose() { SpeechAsync.Scheduler = previous; }
        }

        private static async UniTask<T> AwaitAsync<T>(UniTask<T> task)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                return await SpeechAsync.WaitAsync(task, timeout.Token);
        }

        private static async UniTask VoiceAsync(AIAvatarSpeechPipelineClient pipeline, Connection transport,
            FakeClock clock, double afterSeconds = 0)
        {
            var received = new SpeechCompletionSource<bool>();
            Action handler = () => received.TrySetResult(true);
            pipeline.Voiced += handler;
            try
            {
                clock.Advance(afterSeconds);
                transport.Receive(new JObject { ["type"] = "voiced" });
                await AwaitAsync(received.Task);
            }
            finally { pipeline.Voiced -= handler; }
        }

        private sealed class Connection : IAIAvatarConnection
        {
            private readonly Queue<string> incoming = new Queue<string>();
            private readonly SpeechAsyncSemaphore signal = new SpeechAsyncSemaphore(0);
            private readonly CancellationTokenSource closed = new CancellationTokenSource();
            internal readonly SpeechCompletionSource<JObject> Started = new SpeechCompletionSource<JObject>();
            internal readonly List<JObject> Sent = new List<JObject>();
            internal bool AutoConnected = true;
            internal int DisposeCount;
            internal string SessionId;
            public bool IsOpen { get; private set; }
            public UniTask ConnectAsync(Uri uri, string apiKey, CancellationToken token) { token.ThrowIfCancellationRequested(); IsOpen = true; return UniTask.CompletedTask; }
            public UniTask SendAsync(string message, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var parsed = JObject.Parse(message); Sent.Add(parsed);
                if ((string)parsed["type"] == "start")
                {
                    SessionId = (string)parsed["session_id"];
                    Started.TrySetResult(parsed);
                    if (AutoConnected) Receive(new JObject { ["type"] = "connected", ["context_id"] = parsed["context_id"]?.DeepClone() });
                }
                return UniTask.CompletedTask;
            }
            public async UniTask<string> ReceiveAsync(CancellationToken token)
            {
                using (var source = CancellationTokenSource.CreateLinkedTokenSource(token, closed.Token))
                {
                    await signal.WaitAsync(source.Token);
                    lock (incoming) return incoming.Dequeue();
                }
            }
            internal void Receive(JObject message)
            {
                message["session_id"] = SessionId;
                Receive(message.ToString(Formatting.None));
            }
            internal void Receive(string message)
            {
                lock (incoming) incoming.Enqueue(message);
                signal.Release();
            }
            public void Abort() { IsOpen = false; closed.Cancel(); }
            public void Dispose() { DisposeCount++; Abort(); }
        }

        [Test]
        public async NUnitTask RecognitionUpdatesReplaceOneUtteranceAndConfirmWithRecognizedText()
        {
            var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions { SessionId = "local" }, () => transport);
            var updates = new List<SpeechRecognitionUpdate>();
            var legacy = new List<string>();
            var voiced = 0;
            var confirmed = new SpeechCompletionSource<bool>();
            var started = new SpeechCompletionSource<SpeechPipelineResponse>();
            pipeline.RecognitionUpdated += update =>
            {
                updates.Add(update);
                if (update.Kind == SpeechRecognitionUpdateKind.Confirmed) confirmed.TrySetResult(true);
            };
            pipeline.SpeechDetecting += legacy.Add;
            pipeline.Voiced += () => voiced++;
            pipeline.ResponseReceived += response => { if (response.Type == SpeechPipelineResponseType.Start) started.TrySetResult(response); return UniTask.CompletedTask; };
            try
            {
                await pipeline.ConnectAsync();
                transport.Receive(new JObject { ["type"] = "voiced" });
                transport.Receive(new JObject { ["type"] = "voiced" });
                transport.Receive(new JObject { ["type"] = "info", ["metadata"] = new JObject { ["partial_request_text"] = "hel" } });
                transport.Receive(new JObject { ["type"] = "info", ["metadata"] = new JObject { ["partial_request_text"] = "hello" } });
                transport.Receive(new JObject { ["type"] = "accepted" });
                transport.Receive(new JObject { ["type"] = "start", ["metadata"] = new JObject
                { ["recognized_text"] = "hello!", ["request_text"] = "timestamp and merged history: hello!" } });
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                    await SpeechAsync.WaitAsync(confirmed.Task, timeout.Token);
                var response = await started.Task;
                Assert.That(updates.Count, Is.EqualTo(5));
                Assert.That(updates[0].RecognitionId, Is.Not.Null.And.Not.Empty);
                Assert.That(updates[0].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Started));
                Assert.That(updates[1].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Activity));
                Assert.That(updates[2].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Partial));
                Assert.That(updates[2].Text, Is.EqualTo("hel"));
                Assert.That(updates[3].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Partial));
                Assert.That(updates[3].Text, Is.EqualTo("hello"));
                Assert.That(updates[4].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Confirmed));
                Assert.That(updates[4].Text, Is.EqualTo("hello!"));
                foreach (var update in updates)
                {
                    Assert.That(update.RecognitionId, Is.EqualTo(updates[0].RecognitionId));
                    Assert.That(update.SessionId, Is.EqualTo("local"));
                }
                Assert.That(updates[4].TransactionId, Is.EqualTo(response.TransactionId));
                Assert.That(voiced, Is.EqualTo(2));
                Assert.That(legacy, Is.EqualTo(new[] { "hel", "hello" }));
                transport.Receive(new JObject { ["type"] = "final" });
                await pipeline.DrainAsync();
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask VoicedPublishesImmediateStartedAndActivityWithObservationTime()
        {
            var clock = new FakeClock(); var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport, clock: clock);
            var updates = new List<SpeechRecognitionUpdate>();
            pipeline.RecognitionUpdated += updates.Add;
            try
            {
                await pipeline.ConnectAsync();
                await VoiceAsync(pipeline, transport, clock, 12.5);
                Assert.That(updates.Count, Is.EqualTo(1));
                Assert.That(updates[0].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Started));
                Assert.That(updates[0].ObservedAtSeconds, Is.EqualTo(12.5));
                Assert.That(updates[0].IsSpeechActive, Is.True);
                Assert.That(updates[0].AudioDurationSeconds, Is.Null);
                await VoiceAsync(pipeline, transport, clock, 0.125);
                Assert.That(updates.Count, Is.EqualTo(2));
                Assert.That(updates[1].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Activity));
                Assert.That(updates[1].RecognitionId, Is.EqualTo(updates[0].RecognitionId));
                Assert.That(updates[1].ObservedAtSeconds, Is.EqualTo(12.625));
                Assert.That(updates[1].IsSpeechActive, Is.True);
                Assert.That(updates[1].AudioDurationSeconds, Is.Null);
                Assert.That(string.IsNullOrEmpty(updates[1].Text), Is.True);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask InactiveRecognitionHasNoDisplayTimerAndKeepsItsIdentity()
        {
            using (var scheduler = new DelayObserverScheduler())
            {
                var clock = new FakeClock(); var transport = new Connection();
                var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport, clock: clock);
                var updates = new List<SpeechRecognitionUpdate>();
                var partial = new SpeechCompletionSource<SpeechRecognitionUpdate>();
                pipeline.RecognitionUpdated += update =>
                { updates.Add(update); if (update.Kind == SpeechRecognitionUpdateKind.Partial) partial.TrySetResult(update); };
                try
                {
                    await pipeline.ConnectAsync(); var initialDelays = scheduler.DelayCount;
                    await VoiceAsync(pipeline, transport, clock);
                    await VoiceAsync(pipeline, transport, clock, 30);
                    Assert.That(scheduler.DelayCount, Is.EqualTo(initialDelays), "Recognition activity must not start a display timer.");
                    Assert.That(updates.Count, Is.EqualTo(2));
                    Assert.That(updates[1].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Activity));
                    Assert.That(updates[1].RecognitionId, Is.EqualTo(updates[0].RecognitionId));
                    transport.Receive(new JObject { ["type"] = "info", ["metadata"] = new JObject { ["partial_request_text"] = "later text" } });
                    var recognized = await AwaitAsync(partial.Task);
                    Assert.That(recognized.RecognitionId, Is.EqualTo(updates[0].RecognitionId));
                    Assert.That(recognized.ObservedAtSeconds, Is.EqualTo(30));
                    Assert.That(updates.Exists(update => update.Kind == SpeechRecognitionUpdateKind.Canceled), Is.False);
                    Assert.That(pipeline.IsConnected, Is.True);
                }
                finally { await pipeline.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask ActivityAfterPartialPreservesRecognitionTextAndCancellationPayload()
        {
            var clock = new FakeClock(); var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport, clock: clock);
            var updates = new List<SpeechRecognitionUpdate>();
            var partial = new SpeechCompletionSource<SpeechRecognitionUpdate>();
            pipeline.RecognitionUpdated += update =>
            { updates.Add(update); if (update.Kind == SpeechRecognitionUpdateKind.Partial) partial.TrySetResult(update); };
            try
            {
                await pipeline.ConnectAsync();
                transport.Receive(new JObject { ["type"] = "info", ["metadata"] = new JObject { ["partial_request_text"] = "recognized text" } });
                var recognized = await AwaitAsync(partial.Task);
                await VoiceAsync(pipeline, transport, clock, 0.5);
                Assert.That(updates.Count, Is.EqualTo(2));
                Assert.That(updates[1].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Activity));
                Assert.That(updates[1].RecognitionId, Is.EqualTo(recognized.RecognitionId));
                Assert.That(recognized.Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Partial));
                Assert.That(recognized.Text, Is.EqualTo("recognized text"));
                Assert.That(recognized.IsSpeechActive, Is.Null);
                await pipeline.DisposeAsync();
                Assert.That(updates[2].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Canceled));
                Assert.That(updates[2].Text, Is.EqualTo("recognized text"));
                Assert.That(updates[2].ObservedAtSeconds, Is.EqualTo(0.5));
                Assert.That(updates[2].IsSpeechActive, Is.Null);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask AcceptedRecognitionKeepsItsIdentityUntilConfirmationWithoutLaterActivity(bool withPartial)
        {
            var clock = new FakeClock(); var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport, clock: clock);
            var updates = new List<SpeechRecognitionUpdate>();
            var accepted = new SpeechCompletionSource<SpeechPipelineResponse>();
            var confirmed = new SpeechCompletionSource<SpeechRecognitionUpdate>();
            pipeline.RecognitionUpdated += update =>
            { updates.Add(update); if (update.Kind == SpeechRecognitionUpdateKind.Confirmed) confirmed.TrySetResult(update); };
            pipeline.ResponseReceived += response =>
            { if (response.Type == SpeechPipelineResponseType.Accepted) accepted.TrySetResult(response); return UniTask.CompletedTask; };
            try
            {
                await pipeline.ConnectAsync();
                await VoiceAsync(pipeline, transport, clock);
                if (withPartial) transport.Receive(new JObject { ["type"] = "info", ["metadata"] = new JObject { ["partial_request_text"] = "hello" } });
                transport.Receive(new JObject { ["type"] = "accepted" });
                var turn = await AwaitAsync(accepted.Task);
                var beforeVoiced = updates.Count;
                await VoiceAsync(pipeline, transport, clock, 30);
                Assert.That(updates.Count, Is.EqualTo(beforeVoiced));
                transport.Receive(new JObject { ["type"] = "start", ["metadata"] = new JObject { ["recognized_text"] = "hello!" } });
                var user = await AwaitAsync(confirmed.Task);
                Assert.That(user.RecognitionId, Is.EqualTo(updates[0].RecognitionId));
                Assert.That(user.TransactionId, Is.EqualTo(turn.TransactionId));
                Assert.That(user.Text, Is.EqualTo("hello!"));
                Assert.That(user.ObservedAtSeconds, Is.EqualTo(30));
                Assert.That(user.IsSpeechActive, Is.Null);
                transport.Receive(new JObject { ["type"] = "final" });
                await pipeline.DrainAsync();
                Assert.That(updates.Exists(update => update.Kind == SpeechRecognitionUpdateKind.Canceled), Is.False);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RetiredConnectionActivityCannotAffectTheNewRecognition()
        {
            var clock = new FakeClock(); var transports = new List<Connection>();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () =>
            { var transport = new Connection(); transports.Add(transport); return transport; }, clock: clock);
            var updates = new List<SpeechRecognitionUpdate>(); pipeline.RecognitionUpdated += updates.Add;
            try
            {
                await pipeline.ConnectAsync();
                await VoiceAsync(pipeline, transports[0], clock);
                await pipeline.InterruptAsync();
                await VoiceAsync(pipeline, transports[1], clock, 1);
                var oldId = updates[0].RecognitionId; var currentId = updates[2].RecognitionId;
                Assert.That(updates[1].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Canceled));
                Assert.That(currentId, Is.Not.EqualTo(oldId));
                transports[0].Receive(new JObject { ["type"] = "voiced" });
                await VoiceAsync(pipeline, transports[1], clock, 0.125);
                Assert.That(updates.Count, Is.EqualTo(4));
                Assert.That(updates[3].Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Activity));
                Assert.That(updates[3].RecognitionId, Is.EqualTo(currentId));
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [TestCase("final")]
        [TestCase("canceled")]
        public async NUnitTask TerminalResponseWithoutRecognitionTextCancelsTheAcceptedSpeech(string terminalType)
        {
            var clock = new FakeClock(); var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport, clock: clock);
            var updates = new List<SpeechRecognitionUpdate>();
            var accepted = new SpeechCompletionSource<SpeechPipelineResponse>();
            var canceled = new SpeechCompletionSource<SpeechRecognitionUpdate>();
            pipeline.RecognitionUpdated += update =>
            { updates.Add(update); if (update.Kind == SpeechRecognitionUpdateKind.Canceled) canceled.TrySetResult(update); };
            pipeline.ResponseReceived += response =>
            { if (response.Type == SpeechPipelineResponseType.Accepted) accepted.TrySetResult(response); return UniTask.CompletedTask; };
            try
            {
                await pipeline.ConnectAsync();
                await VoiceAsync(pipeline, transport, clock);
                transport.Receive(new JObject { ["type"] = "accepted" });
                var turn = await AwaitAsync(accepted.Task);
                clock.Advance(2);
                transport.Receive(new JObject { ["type"] = terminalType });
                var ended = await AwaitAsync(canceled.Task);
                await pipeline.DrainAsync();
                Assert.That(updates.Count, Is.EqualTo(2));
                Assert.That(ended.RecognitionId, Is.EqualTo(updates[0].RecognitionId));
                Assert.That(ended.TransactionId, Is.EqualTo(turn.TransactionId));
                Assert.That(ended.ObservedAtSeconds, Is.EqualTo(2));
                Assert.That(ended.IsSpeechActive, Is.Null);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [TestCase("interrupt")]
        [TestCase("reset")]
        [TestCase("stop")]
        [TestCase("disconnect")]
        [TestCase("dispose")]
        public async NUnitTask RecognitionControlBoundaryCancelsCurrentPartial(string boundary)
        {
            var transports = new List<Connection>();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () =>
            { var connection = new Connection(); transports.Add(connection); return connection; });
            var updates = new List<SpeechRecognitionUpdate>();
            var partial = new SpeechCompletionSource<bool>();
            var canceled = new SpeechCompletionSource<bool>();
            pipeline.RecognitionUpdated += update =>
            {
                updates.Add(update);
                if (update.Kind == SpeechRecognitionUpdateKind.Partial) partial.TrySetResult(true);
                if (update.Kind == SpeechRecognitionUpdateKind.Canceled) canceled.TrySetResult(true);
            };
            try
            {
                await pipeline.ConnectAsync();
                transports[0].Receive(new JObject { ["type"] = "info", ["metadata"] = new JObject { ["partial_request_text"] = "pending" } });
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    await SpeechAsync.WaitAsync(partial.Task, timeout.Token);
                    if (boundary == "interrupt") await pipeline.InterruptAsync();
                    else if (boundary == "reset") await pipeline.ResetAsync();
                    else if (boundary == "stop") transports[0].Receive(new JObject { ["type"] = "stop" });
                    else if (boundary == "disconnect") transports[0].Receive("invalid json");
                    else await pipeline.DisposeAsync();
                    await SpeechAsync.WaitAsync(canceled.Task, timeout.Token);
                }
                Assert.That(updates.Count, Is.EqualTo(2));
                Assert.That(updates[1].RecognitionId, Is.EqualTo(updates[0].RecognitionId));
                if (boundary != "stop")
                {
                    transports[0].Receive(new JObject { ["type"] = "info", ["metadata"] = new JObject { ["partial_request_text"] = "stale" } });
                    await SpeechAsync.Yield();
                    Assert.That(updates.Count, Is.EqualTo(2));
                }
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [TestCase("interrupt")]
        [TestCase("reset")]
        [TestCase("stop")]
        [TestCase("disconnect")]
        [TestCase("dispose")]
        public async NUnitTask RecognitionControlBoundaryCancelsSpeechStartedBeforeAnyPartial(string boundary)
        {
            var transports = new List<Connection>();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () =>
            { var connection = new Connection(); transports.Add(connection); return connection; });
            var updates = new List<SpeechRecognitionUpdate>();
            var started = new SpeechCompletionSource<bool>();
            var canceled = new SpeechCompletionSource<bool>();
            pipeline.RecognitionUpdated += update =>
            {
                updates.Add(update);
                if (update.Kind == SpeechRecognitionUpdateKind.Started) started.TrySetResult(true);
                if (update.Kind == SpeechRecognitionUpdateKind.Canceled) canceled.TrySetResult(true);
            };
            try
            {
                await pipeline.ConnectAsync();
                transports[0].Receive(new JObject { ["type"] = "voiced" });
                await AwaitAsync(started.Task);
                if (boundary == "interrupt") await pipeline.InterruptAsync();
                else if (boundary == "reset") await pipeline.ResetAsync();
                else if (boundary == "stop") transports[0].Receive(new JObject { ["type"] = "stop" });
                else if (boundary == "disconnect") transports[0].Receive("invalid json");
                else await pipeline.DisposeAsync();
                await AwaitAsync(canceled.Task);

                Assert.That(updates.Count, Is.EqualTo(2));
                Assert.That(updates[1].RecognitionId, Is.EqualTo(updates[0].RecognitionId));
                Assert.That(string.IsNullOrEmpty(updates[1].Text), Is.True);
                Assert.That(updates[1].TransactionId, Is.Null);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ConnectWaitsForMatchingConnectedAcknowledgement()
        {
            var transport = new Connection { AutoConnected = false };
            var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions { SessionId = "local" }, () => transport);
            try
            {
                var connecting = SpeechAsync.Share(pipeline.ConnectAsync());
                await transport.Started.Task;
                Assert.That(connecting.Status.IsCompleted(), Is.False);
                Assert.That(pipeline.IsConnected, Is.False);
                transport.Receive("{\"type\":\"connected\",\"session_id\":\"another-session\"}");
                await SpeechAsync.Yield();
                Assert.That(connecting.Status.IsCompleted(), Is.False);
                transport.Receive(new JObject { ["type"] = "connected" });
                await connecting;
                Assert.That(pipeline.IsConnected, Is.True);
                Assert.That(pipeline.SessionId, Is.EqualTo("local"));
                Assert.That(pipeline.RemoteSessionId, Is.Not.EqualTo(pipeline.SessionId));
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask InterruptChangesRemoteSessionAndPreservesAuthoritativeContext()
        {
            var transports = new List<Connection>();
            var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions { SessionId = "local" }, () =>
            {
                var result = new Connection(); transports.Add(result); return result;
            });
            var responses = new List<SpeechPipelineResponse>();
            var started = new SpeechCompletionSource<bool>();
            pipeline.ResponseReceived += response =>
            {
                responses.Add(response);
                if (response.Type == SpeechPipelineResponseType.Start) started.TrySetResult(true);
                return UniTask.CompletedTask;
            };
            try
            {
                await pipeline.ConnectAsync();
                var oldId = pipeline.RemoteSessionId;
                transports[0].Receive(new JObject { ["type"] = "accepted" });
                transports[0].Receive(new JObject { ["type"] = "start", ["context_id"] = "server-context" });
                await started.Task;
                var draining = SpeechAsync.Share(pipeline.DrainAsync());
                await pipeline.InterruptAsync();
                await draining;
                Assert.That(pipeline.SessionId, Is.EqualTo("local"));
                Assert.That(pipeline.RemoteSessionId, Is.Not.EqualTo(oldId));
                Assert.That(pipeline.ContextId, Is.EqualTo("server-context"));
                Assert.That((string)transports[1].Sent[0]["context_id"], Is.EqualTo("server-context"));
                Assert.That(transports[0].DisposeCount, Is.EqualTo(1));
                Assert.That(responses.FindAll(item => item.Type == SpeechPipelineResponseType.Canceled).Count, Is.EqualTo(1));
                transports[0].Receive(new JObject { ["type"] = "chunk", ["text"] = "stale" });
                await SpeechAsync.Yield();
                Assert.That(responses.Exists(item => item.Text == "stale"), Is.False);
                await pipeline.ResetAsync("different-context");
                Assert.That((string)transports[2].Sent[0]["context_id"], Is.EqualTo("different-context"));
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DisposeFromConnectionFactoryRetiresTheUnpublishedTransport()
        {
            var transport = new Connection();
            AIAvatarSpeechPipelineClient pipeline = null;
            UniTask? disposing = null;
            pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () =>
            {
                disposing = pipeline.DisposeAsync();
                return transport;
            });
            try
            {
                await SpeechAsyncAssert.ThrowsAsync<ObjectDisposedException>(() => pipeline.ConnectAsync());
                Assert.That(disposing.HasValue, Is.True);
                await disposing.Value;
                Assert.That(transport.DisposeCount, Is.EqualTo(1));
                Assert.That(transport.Started.Task.Status.IsCompleted(), Is.False);
                Assert.That(pipeline.IsConnected, Is.False);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DisposeDuringHandshakeCancelsConnectAndSettlesBeforeReturning()
        {
            var transport = new Connection { AutoConnected = false };
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport);
            var connecting = SpeechAsync.Share(pipeline.ConnectAsync());
            await transport.Started.Task;
            await pipeline.DisposeAsync();
            await SpeechAsyncAssert.ThrowsAsync<OperationCanceledException>(async () => await connecting);
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
            Assert.That(pipeline.IsConnected, Is.False);
            await pipeline.DisposeAsync();
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask InvalidWireMessageFailsTheActiveTurnAndReleasesDrain()
        {
            var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport);
            var accepted = new SpeechCompletionSource<bool>();
            var errors = new List<Exception>();
            pipeline.Error += errors.Add;
            pipeline.ResponseReceived += response =>
            {
                if (response.Type == SpeechPipelineResponseType.Accepted) accepted.TrySetResult(true);
                return UniTask.CompletedTask;
            };
            try
            {
                await pipeline.ConnectAsync();
                transport.Receive(new JObject { ["type"] = "accepted" });
                await accepted.Task;
                var draining = SpeechAsync.Share(pipeline.DrainAsync());
                transport.Receive("invalid json");
                await SpeechAsyncAssert.ThrowsAsync<JsonReaderException>(async () => await draining);
                Assert.That(errors.Count, Is.EqualTo(1));
                Assert.That(pipeline.IsConnected, Is.False);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask AnAcceptedTurnWithoutATerminalEventTimesOutOnce()
        {
            var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions { RequestTimeout = TimeSpan.FromMilliseconds(25) }, () => transport);
            var accepted = new SpeechCompletionSource<bool>();
            var errors = new List<Exception>();
            pipeline.Error += errors.Add;
            pipeline.ResponseReceived += response =>
            {
                if (response.Type == SpeechPipelineResponseType.Accepted) accepted.TrySetResult(true);
                return UniTask.CompletedTask;
            };
            try
            {
                await pipeline.ConnectAsync();
                transport.Receive(new JObject { ["type"] = "accepted" });
                await accepted.Task;
                await SpeechAsyncAssert.ThrowsAsync<TimeoutException>(() => pipeline.DrainAsync());
                await SpeechAsync.Yield();
                Assert.That(errors.Count, Is.EqualTo(1));
                Assert.That(pipeline.IsConnected, Is.False);
            }
            finally { await pipeline.DisposeAsync(); }
        }
    }
}

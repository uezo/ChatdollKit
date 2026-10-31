using System;
using System.Collections.Concurrent;
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

        private sealed class ManualAudioScheduler : ISpeechAsyncScheduler, IDisposable
        {
            private sealed class Delay
            {
                internal double Due;
                internal readonly SpeechCompletionSource<bool> Completion = new SpeechCompletionSource<bool>();
                internal CancellationTokenRegistration Cancellation;
            }

            private readonly ISpeechAsyncScheduler previous = SpeechAsync.Scheduler;
            private readonly ConcurrentQueue<Action> continuations = new ConcurrentQueue<Action>();
            private readonly List<Delay> delays = new List<Delay>();
            internal readonly FakeClock Clock = new FakeClock();

            internal ManualAudioScheduler() { SpeechAsync.Scheduler = this; }
            public void Post(Action continuation) => continuations.Enqueue(continuation);
            public UniTask DelayAsync(TimeSpan delay, CancellationToken token)
            {
                var pending = new Delay { Due = Clock.ElapsedSeconds + delay.TotalSeconds };
                pending.Cancellation = token.Register(() => pending.Completion.TrySetCanceled(token));
                delays.Add(pending);
                return WaitDelayAsync(pending);
            }

            private static async UniTask WaitDelayAsync(Delay pending)
            {
                try { await pending.Completion.Task; }
                finally { pending.Cancellation.Dispose(); }
            }

            internal void Drain()
            {
                var count = 0;
                while (continuations.TryDequeue(out var continuation))
                {
                    Assert.That(++count, Is.LessThan(10000), "Scheduled work must settle without a busy loop.");
                    continuation();
                }
                delays.RemoveAll(delay => delay.Completion.Task.Status.IsCompleted());
            }

            internal void Advance(double seconds)
            {
                Clock.Advance(seconds);
                // Complete only the delays already scheduled. Newly scheduled audio must wait
                // for its own cadence even when the clock jumps far beyond the previous tick.
                foreach (var delay in delays.ToArray())
                    if (delay.Due <= Clock.ElapsedSeconds + 0.0000001) delay.Completion.TrySetResult(true);
                Drain();
            }

            internal void Complete(UniTask operation)
            {
                Drain();
                Assert.That(operation.Status.IsCompleted(), Is.True, "Operation must finish without advancing the audio clock.");
                operation.GetAwaiter().GetResult();
            }

            public void Dispose()
            {
                foreach (var delay in delays.ToArray()) delay.Completion.TrySetCanceled();
                Drain();
                SpeechAsync.Scheduler = previous;
            }
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

        private static async UniTask<SpeechPipelineResponse> ReceiveResponseAsync(AIAvatarSpeechPipelineClient pipeline,
            Connection transport, JObject message, SpeechPipelineResponseType expectedType)
        {
            var received = new SpeechCompletionSource<SpeechPipelineResponse>();
            Func<SpeechPipelineResponse, UniTask> handler = response =>
            {
                if (response.Type == expectedType) received.TrySetResult(response);
                return UniTask.CompletedTask;
            };
            pipeline.ResponseReceived += handler;
            try
            {
                transport.Receive(message);
                return await AwaitAsync(received.Task);
            }
            finally { pipeline.ResponseReceived -= handler; }
        }

        private sealed class Connection : IAIAvatarConnection
        {
            private readonly Queue<string> incoming = new Queue<string>();
            private readonly SpeechAsyncSemaphore signal = new SpeechAsyncSemaphore(0);
            private readonly CancellationTokenSource closed = new CancellationTokenSource();
            internal readonly SpeechCompletionSource<JObject> Started = new SpeechCompletionSource<JObject>();
            internal readonly List<JObject> Sent = new List<JObject>();
            internal bool AutoConnected = true;
            internal Exception ConfigSendFailure;
            internal Func<JObject, CancellationToken, UniTask> BeforeSendAsync;
            internal bool ConcurrentSendDetected;
            private int activeSends;
            internal int DisposeCount;
            internal string SessionId;
            public bool IsOpen { get; private set; }
            public UniTask ConnectAsync(Uri uri, string apiKey, CancellationToken token) { token.ThrowIfCancellationRequested(); IsOpen = true; return UniTask.CompletedTask; }
            public async UniTask SendAsync(string message, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var parsed = JObject.Parse(message);
                if ((string)parsed["type"] == "config" && ConfigSendFailure != null) throw ConfigSendFailure;
                if (Interlocked.Increment(ref activeSends) > 1) ConcurrentSendDetected = true;
                try
                {
                    if (BeforeSendAsync != null) await BeforeSendAsync(parsed, token);
                    token.ThrowIfCancellationRequested();
                    Sent.Add(parsed);
                    if ((string)parsed["type"] == "start")
                    {
                        SessionId = (string)parsed["session_id"];
                        Started.TrySetResult(parsed);
                        if (AutoConnected) Receive(new JObject { ["type"] = "connected", ["context_id"] = parsed["context_id"]?.DeepClone() });
                    }
                }
                finally { Interlocked.Decrement(ref activeSends); }
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

        private static List<JObject> AudioMessages(Connection transport) =>
            transport.Sent.FindAll(message => (string)message["type"] == "data");

        private static JObject NodMessage(string id = "nod") => new JObject
        {
            ["type"] = "chunk", ["text"] = "", ["voice_text"] = "",
            ["audio_data"] = "UklGRiYAAABXQVZFZm10IBAAAAABAAEAgD4AAAB9AAACABAAZGF0YQIAAAAAAA==",
            ["metadata"] = new JObject { ["nod"] = true, ["nod_id"] = id, ["recording_id"] = "recording" }
        };

        [TestCase("before_accepted")]
        [TestCase("during_turn")]
        [TestCase("after_final")]
        public async NUnitTask NodIsIndependentOfResponseTurnsAndDoesNotChangeRecognitionOrAudioInput(string timing)
        {
            var clock = new FakeClock(); var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport, clock: clock);
            var responses = new List<SpeechPipelineResponse>();
            var updates = new List<SpeechRecognitionUpdate>();
            var errors = new List<Exception>();
            pipeline.ResponseReceived += response => { responses.Add(response); return UniTask.CompletedTask; };
            pipeline.RecognitionUpdated += updates.Add;
            pipeline.Error += errors.Add;
            try
            {
                await pipeline.ConnectAsync();
                SpeechPipelineResponse turn = null;
                if (timing != "before_accepted")
                    turn = await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "accepted" }, SpeechPipelineResponseType.Accepted);
                if (timing == "after_final")
                {
                    await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "final" }, SpeechPipelineResponseType.Final);
                    await pipeline.DrainAsync();
                    turn = null;
                }
                await VoiceAsync(pipeline, transport, clock);
                var recognitionCount = updates.Count;
                var responseCount = responses.Count;
                var nod = await ReceiveResponseAsync(pipeline, transport, NodMessage(), SpeechPipelineResponseType.Backchannel);
                Assert.That(nod.TransactionId, Is.Null);
                Assert.That(nod.IsTerminal, Is.False);
                Assert.That((string)nod.Metadata["nod_id"], Is.EqualTo("nod"));
                CollectionAssert.AreEqual(Convert.FromBase64String((string)NodMessage()["audio_data"]), nod.AudioData);
                Assert.That(updates.Count, Is.EqualTo(recognitionCount));
                Assert.That(responses.Count, Is.EqualTo(responseCount + 1));
                Assert.That(pipeline.IsConnected, Is.True);
                await pipeline.ProcessAudioSamplesAsync(new byte[] { 1, 2 });
                Assert.That(AudioMessages(transport).Count, Is.EqualTo(1));
                if (turn == null)
                {
                    await pipeline.DrainAsync();
                    turn = await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "accepted" }, SpeechPipelineResponseType.Accepted);
                }
                var ordinary = NodMessage(); ordinary["metadata"] = null;
                var chunk = await ReceiveResponseAsync(pipeline, transport, ordinary, SpeechPipelineResponseType.Chunk);
                Assert.That(chunk.TransactionId, Is.EqualTo(turn.TransactionId));
                CollectionAssert.AreEqual(nod.AudioData, chunk.AudioData);
                var final = await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "final" }, SpeechPipelineResponseType.Final);
                await pipeline.DrainAsync();
                Assert.That(final.TransactionId, Is.EqualTo(turn.TransactionId));
                Assert.That(errors, Is.Empty);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ConnectionFailureAfterNodDoesNotPublishStopWithOrWithoutAnAcceptedTurn(bool hasTurn)
        {
            var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport);
            var failed = new SpeechCompletionSource<Exception>();
            var terminalEvents = new List<SpeechPipelineResponseType>();
            pipeline.ResponseReceived += response =>
            {
                if (response.Type == SpeechPipelineResponseType.Error || response.Type == SpeechPipelineResponseType.Stop)
                    terminalEvents.Add(response.Type);
                return UniTask.CompletedTask;
            };
            pipeline.Error += error => failed.TrySetResult(error);
            try
            {
                await pipeline.ConnectAsync();
                if (hasTurn)
                    await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "accepted" }, SpeechPipelineResponseType.Accepted);
                await ReceiveResponseAsync(pipeline, transport, NodMessage(), SpeechPipelineResponseType.Backchannel);
                var draining = hasTurn ? SpeechAsync.Share(pipeline.DrainAsync()) : UniTask.CompletedTask;
                transport.Receive("invalid json");
                Assert.That(await AwaitAsync(failed.Task), Is.TypeOf<JsonReaderException>());
                if (hasTurn) await SpeechAsyncAssert.ThrowsAsync<JsonReaderException>(async () => await draining);
                Assert.That(terminalEvents, Is.EqualTo(hasTurn
                    ? new[] { SpeechPipelineResponseType.Error }
                    : Array.Empty<SpeechPipelineResponseType>()));
                Assert.That(pipeline.IsConnected, Is.False);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ReplacingAConnectionPublishesOneStopAndRejectsItsLateNod(bool reset)
        {
            var transports = new List<Connection>();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () =>
            { var transport = new Connection(); transports.Add(transport); return transport; });
            var responses = new List<SpeechPipelineResponse>();
            pipeline.ResponseReceived += response => { responses.Add(response); return UniTask.CompletedTask; };
            try
            {
                await pipeline.ConnectAsync();
                await ReceiveResponseAsync(pipeline, transports[0], NodMessage(), SpeechPipelineResponseType.Backchannel);
                if (reset) await pipeline.ResetAsync("replacement-context"); else await pipeline.InterruptAsync();
                var stops = responses.FindAll(response => response.Type == SpeechPipelineResponseType.Stop);
                Assert.That(stops.Count, Is.EqualTo(1));
                Assert.That(stops[0].TransactionId, Is.Null);
                Assert.That(stops[0].ContextId, Is.EqualTo(pipeline.ContextId));
                if (reset) Assert.That(stops[0].ContextId, Is.EqualTo("replacement-context"));
                transports[0].Receive(NodMessage("stale"));
                await ReceiveResponseAsync(pipeline, transports[1], NodMessage("fresh"), SpeechPipelineResponseType.Backchannel);
                Assert.That(responses.Exists(response => (string)response.Metadata?["nod_id"] == "stale"), Is.False);
                Assert.That(pipeline.IsConnected, Is.True);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask WireStopAfterNodIsDeliveredWithoutDisconnecting()
        {
            var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport);
            try
            {
                await pipeline.ConnectAsync();
                await ReceiveResponseAsync(pipeline, transport, NodMessage(), SpeechPipelineResponseType.Backchannel);
                var stop = await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "stop" }, SpeechPipelineResponseType.Stop);
                Assert.That(stop.TransactionId, Is.Null);
                Assert.That(stop.SessionId, Is.EqualTo(pipeline.SessionId));
                Assert.That(pipeline.IsConnected, Is.True);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public void StandaloneClientsDoNotSendIdleAudioByDefault()
        {
            using (var scheduler = new ManualAudioScheduler())
            {
                var options = new AIAvatarSpeechPipelineOptions();
                var transport = new Connection();
                var pipeline = new AIAvatarSpeechPipelineClient(options, () => transport, scheduler.Clock);
                try
                {
                    Assert.That(options.SendSilenceWhenIdle, Is.False);
                    Assert.That(options.InputSampleRate, Is.EqualTo(16000));
                    Assert.That(options.SamplesPerMessage, Is.EqualTo(512));
                    scheduler.Complete(pipeline.ConnectAsync());
                    scheduler.Advance(60);
                    Assert.That(AudioMessages(transport), Is.Empty);
                }
                finally { scheduler.Complete(pipeline.DisposeAsync()); }
            }
        }

        [TestCase(16000, 512)]
        [TestCase(8000, 800)]
        public void IdleSilenceUsesConfiguredPcmSizeAndCadenceWithoutCatchUpBursts(int sampleRate, int samplesPerMessage)
        {
            using (var scheduler = new ManualAudioScheduler())
            {
                var transport = new Connection();
                var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions
                {
                    SendSilenceWhenIdle = true, InputSampleRate = sampleRate, SamplesPerMessage = samplesPerMessage
                }, () => transport, scheduler.Clock);
                var frameDuration = (double)samplesPerMessage / sampleRate;
                var idleThreshold = Math.Max(0.1, 3 * frameDuration);
                try
                {
                    scheduler.Complete(pipeline.ConnectAsync());
                    for (var index = 0; index < 4; index++)
                    {
                        scheduler.Advance(frameDuration);
                        if (scheduler.Clock.ElapsedSeconds + 0.0000001 < idleThreshold)
                            Assert.That(AudioMessages(transport), Is.Empty, "Ordinary microphone cadence must have an inactivity grace period.");
                    }
                    var beforePause = AudioMessages(transport).Count;
                    Assert.That(beforePause, Is.GreaterThan(0));
                    scheduler.Advance(30);
                    Assert.That(AudioMessages(transport).Count, Is.EqualTo(beforePause + 1), "A delayed tick must not send a backlog of silence.");
                    scheduler.Advance(frameDuration);
                    Assert.That(AudioMessages(transport).Count, Is.EqualTo(beforePause + 2));
                    foreach (var message in AudioMessages(transport))
                    {
                        Assert.That((string)message["session_id"], Is.EqualTo(pipeline.RemoteSessionId));
                        var pcm = Convert.FromBase64String((string)message["audio_data"]);
                        Assert.That(pcm.Length, Is.EqualTo(samplesPerMessage * 2));
                        Assert.That(Array.TrueForAll(pcm, sample => sample == 0), Is.True);
                    }
                    scheduler.Complete(pipeline.DrainAsync());
                }
                finally { scheduler.Complete(pipeline.DisposeAsync()); }
            }
        }

        [Test]
        public void RecentAndInFlightRealAudioPreventIdleSilenceAndConcurrentSends()
        {
            using (var scheduler = new ManualAudioScheduler())
            {
                var transport = new Connection();
                var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions
                { SendSilenceWhenIdle = true }, () => transport, scheduler.Clock);
                var realAudio = new byte[] { 1, 2, 3, 4 };
                var release = new SpeechCompletionSource<bool>();
                var entered = false;
                try
                {
                    scheduler.Complete(pipeline.ConnectAsync());
                    for (var index = 0; index < 8; index++)
                    {
                        scheduler.Complete(pipeline.ProcessAudioSamplesAsync(realAudio));
                        scheduler.Advance(0.032);
                    }
                    Assert.That(AudioMessages(transport).Count, Is.EqualTo(8));
                    foreach (var message in AudioMessages(transport))
                        CollectionAssert.AreEqual(realAudio, Convert.FromBase64String((string)message["audio_data"]));

                    transport.BeforeSendAsync = (message, token) =>
                    {
                        if ((string)message["type"] != "data") return UniTask.CompletedTask;
                        entered = true;
                        return SpeechAsync.WaitAsync(release.Task.AsUniTask(), token);
                    };
                    var first = pipeline.ProcessAudioSamplesAsync(realAudio);
                    scheduler.Drain();
                    Assert.That(entered, Is.True);
                    var second = pipeline.ProcessAudioSamplesAsync(realAudio);
                    scheduler.Advance(10);
                    Assert.That(AudioMessages(transport).Count, Is.EqualTo(8));
                    Assert.That(transport.ConcurrentSendDetected, Is.False);

                    release.TrySetResult(true);
                    scheduler.Complete(first);
                    scheduler.Complete(second);
                    transport.BeforeSendAsync = null;
                    Assert.That(AudioMessages(transport).Count, Is.EqualTo(10), "Pending real audio must take priority over idle silence.");
                    scheduler.Advance(0.032);
                    Assert.That(AudioMessages(transport).Count, Is.EqualTo(10), "Completing real audio must refresh the inactivity period.");
                    scheduler.Advance(0.128);
                    Assert.That(AudioMessages(transport).Count, Is.EqualTo(11));
                    Assert.That(transport.ConcurrentSendDetected, Is.False);
                }
                finally
                {
                    release.TrySetResult(true);
                    scheduler.Complete(pipeline.DisposeAsync());
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReplacingConnectionRetiresItsSilenceLoopAndDisposeStopsTheReplacement(bool reset)
        {
            using (var scheduler = new ManualAudioScheduler())
            {
                var transports = new List<Connection>();
                var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions
                { SendSilenceWhenIdle = true }, () =>
                { var next = new Connection(); transports.Add(next); return next; }, scheduler.Clock);
                try
                {
                    scheduler.Complete(pipeline.ConnectAsync());
                    scheduler.Advance(0.128);
                    Assert.That(AudioMessages(transports[0]).Count, Is.EqualTo(1));
                    scheduler.Complete(reset ? pipeline.ResetAsync("next") : pipeline.InterruptAsync());
                    Assert.That(transports.Count, Is.EqualTo(2));
                    Assert.That(transports[0].DisposeCount, Is.EqualTo(1));
                    scheduler.Advance(0.128);
                    Assert.That(AudioMessages(transports[0]).Count, Is.EqualTo(1));
                    Assert.That(AudioMessages(transports[1]).Count, Is.EqualTo(1));
                    Assert.That((string)AudioMessages(transports[1])[0]["session_id"], Is.EqualTo(pipeline.RemoteSessionId));
                    scheduler.Complete(pipeline.DisposeAsync());
                    scheduler.Advance(30);
                    Assert.That(transports.Count, Is.EqualTo(2));
                    Assert.That(AudioMessages(transports[1]).Count, Is.EqualTo(1));
                    Assert.That(transports[1].DisposeCount, Is.EqualTo(1));
                }
                finally { scheduler.Complete(pipeline.DisposeAsync()); }
            }
        }

        [Test]
        public void DisconnectedSilenceLoopDoesNotReconnectWithoutAnExplicitRequest()
        {
            using (var scheduler = new ManualAudioScheduler())
            {
                var transport = new Connection();
                var createdConnections = 0;
                var errors = new List<Exception>();
                var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions
                { SendSilenceWhenIdle = true }, () => { createdConnections++; return transport; }, scheduler.Clock);
                pipeline.Error += errors.Add;
                try
                {
                    scheduler.Complete(pipeline.ConnectAsync());
                    scheduler.Advance(0.128);
                    transport.Receive("invalid json");
                    scheduler.Drain();
                    Assert.That(pipeline.IsConnected, Is.False);
                    Assert.That(errors.Count, Is.EqualTo(1));
                    scheduler.Advance(30);
                    Assert.That(createdConnections, Is.EqualTo(1));
                    Assert.That(AudioMessages(transport).Count, Is.EqualTo(1));
                }
                finally { scheduler.Complete(pipeline.DisposeAsync()); }
            }
        }

        [Test]
        public void DisposeCancelsAnInFlightSilenceSend()
        {
            using (var scheduler = new ManualAudioScheduler())
            {
                var transport = new Connection();
                var release = new SpeechCompletionSource<bool>();
                var entered = false;
                transport.BeforeSendAsync = (message, token) =>
                {
                    if ((string)message["type"] != "data") return UniTask.CompletedTask;
                    entered = true;
                    return SpeechAsync.WaitAsync(release.Task.AsUniTask(), token);
                };
                var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions
                { SendSilenceWhenIdle = true }, () => transport, scheduler.Clock);
                var errors = new List<Exception>();
                pipeline.Error += errors.Add;
                try
                {
                    scheduler.Complete(pipeline.ConnectAsync());
                    scheduler.Advance(0.128);
                    Assert.That(entered, Is.True);
                    scheduler.Complete(pipeline.DisposeAsync());
                    Assert.That(transport.DisposeCount, Is.EqualTo(1));
                    Assert.That(AudioMessages(transport), Is.Empty);
                    Assert.That(errors, Is.Empty, "Expected send cancellation during disposal is not a transport failure.");
                    release.TrySetResult(true);
                    scheduler.Advance(30);
                    Assert.That(AudioMessages(transport), Is.Empty);
                }
                finally
                {
                    release.TrySetResult(true);
                    scheduler.Complete(pipeline.DisposeAsync());
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask BargeInBeforeConnectIsIncludedInStartAndPreservesOtherMetadata(bool allowBargeIn)
        {
            var transport = new Connection();
            var createdConnections = 0;
            var metadata = new JObject
            {
                ["barge_in_enabled"] = !allowBargeIn,
                ["custom"] = new JObject { ["value"] = "preserved" }
            };
            var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions { Metadata = metadata }, () =>
            { createdConnections++; return transport; });
            try
            {
                await pipeline.SetAllowBargeInAsync(allowBargeIn);
                Assert.That(createdConnections, Is.Zero, "Setting the policy must not open a connection.");
                Assert.That(pipeline.IsConnected, Is.False);
                await pipeline.ConnectAsync();

                Assert.That(transport.Sent.Count, Is.EqualTo(1));
                var start = transport.Sent[0];
                Assert.That((string)start["type"], Is.EqualTo("start"));
                Assert.That(start["metadata"]?["barge_in_enabled"]?.Type, Is.EqualTo(JTokenType.Boolean));
                Assert.That((bool?)start["metadata"]?["barge_in_enabled"], Is.EqualTo(allowBargeIn));
                Assert.That((string)start["metadata"]?["custom"]?["value"], Is.EqualTo("preserved"));
                Assert.That((bool)metadata["barge_in_enabled"], Is.EqualTo(!allowBargeIn), "Caller-owned metadata must remain unchanged.");
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RuntimeBargeInChangesSendConfigOnlyWhenTheValueChanges()
        {
            var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport);
            try
            {
                await pipeline.SetAllowBargeInAsync(false);
                await pipeline.ConnectAsync();
                await pipeline.SetAllowBargeInAsync(false);
                Assert.That(transport.Sent.Count, Is.EqualTo(1));

                await pipeline.SetAllowBargeInAsync(true);
                await pipeline.SetAllowBargeInAsync(true);
                await pipeline.SetAllowBargeInAsync(false);
                await pipeline.SetAllowBargeInAsync(false);

                var configs = transport.Sent.FindAll(message => (string)message["type"] == "config");
                Assert.That(configs.Count, Is.EqualTo(2));
                Assert.That((bool?)configs[0]["metadata"]?["barge_in_enabled"], Is.True);
                Assert.That((bool?)configs[1]["metadata"]?["barge_in_enabled"], Is.False);
                foreach (var config in configs)
                {
                    Assert.That((string)config["session_id"], Is.EqualTo(pipeline.RemoteSessionId));
                    Assert.That(config["metadata"]?["barge_in_enabled"]?.Type, Is.EqualTo(JTokenType.Boolean));
                }
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask RuntimeBargeInSettingSurvivesInterruptAndContextReset(bool allowBargeIn)
        {
            var transports = new List<Connection>();
            var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions
            { Metadata = new JObject { ["custom"] = "preserved" } }, () =>
            { var transport = new Connection(); transports.Add(transport); return transport; });
            try
            {
                await pipeline.SetAllowBargeInAsync(!allowBargeIn);
                await pipeline.ConnectAsync();
                await pipeline.SetAllowBargeInAsync(allowBargeIn);
                await pipeline.InterruptAsync();
                await pipeline.SetAllowBargeInAsync(allowBargeIn);
                await pipeline.ResetAsync("next-context");
                await pipeline.SetAllowBargeInAsync(allowBargeIn);

                Assert.That(transports.Count, Is.EqualTo(3));
                for (var index = 1; index < transports.Count; index++)
                {
                    Assert.That(transports[index].Sent.Count, Is.EqualTo(1), "Reconnect must carry the setting in start without an extra config.");
                    var start = transports[index].Sent[0];
                    Assert.That((string)start["type"], Is.EqualTo("start"));
                    Assert.That((bool?)start["metadata"]?["barge_in_enabled"], Is.EqualTo(allowBargeIn));
                    Assert.That((string)start["metadata"]?["custom"], Is.EqualTo("preserved"));
                }
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask FailedBargeInConfigCanBeRetriedWithTheSameValue()
        {
            var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport);
            try
            {
                await pipeline.SetAllowBargeInAsync(false);
                await pipeline.ConnectAsync();
                transport.ConfigSendFailure = new InvalidOperationException("Simulated config send failure.");
                await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(() => pipeline.SetAllowBargeInAsync(true));
                transport.ConfigSendFailure = null;
                await pipeline.SetAllowBargeInAsync(true);

                var configs = transport.Sent.FindAll(message => (string)message["type"] == "config");
                Assert.That(configs.Count, Is.EqualTo(1));
                Assert.That((bool?)configs[0]["metadata"]?["barge_in_enabled"], Is.True);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask CanceledBargeInUpdateDoesNotPreventLaterConfigWithTheSameValue()
        {
            var transport = new Connection();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport);
            try
            {
                await pipeline.SetAllowBargeInAsync(false);
                await pipeline.ConnectAsync();
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    await SpeechAsyncAssert.ThrowsAsync<OperationCanceledException>(() => pipeline.SetAllowBargeInAsync(true, cancellation.Token));
                }
                Assert.That(transport.Sent.Count, Is.EqualTo(1));
                await pipeline.SetAllowBargeInAsync(true);
                Assert.That(transport.Sent.Count, Is.EqualTo(2));
                Assert.That((string)transport.Sent[1]["type"], Is.EqualTo("config"));
                Assert.That((bool?)transport.Sent[1]["metadata"]?["barge_in_enabled"], Is.True);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DebugLogsAreDisabledByDefaultAndCanBeToggledWhileConnected()
        {
            var transport = new Connection();
            var logs = new ConcurrentQueue<string>();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport) { DebugLog = logs.Enqueue };
            try
            {
                Assert.That(pipeline.DebugLogEnabled, Is.False);
                await pipeline.SetAllowBargeInAsync(false);
                await pipeline.ConnectAsync();
                await pipeline.SetAllowBargeInAsync(true);
                await pipeline.ProcessAudioSamplesAsync(new byte[] { 1, 2 });
                await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "accepted" }, SpeechPipelineResponseType.Accepted);
                await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "final" }, SpeechPipelineResponseType.Final);
                await pipeline.DrainAsync();
                Assert.That(logs.IsEmpty, Is.True);

                var sessionId = pipeline.RemoteSessionId;
                pipeline.DebugLogEnabled = true;
                await pipeline.SetAllowBargeInAsync(false);
                Assert.That(Array.Exists(logs.ToArray(), line => line.Contains(" config.sent ")), Is.True);
                pipeline.DebugLogEnabled = false;
                var loggedCount = logs.Count;
                await pipeline.SetAllowBargeInAsync(true);
                await pipeline.ProcessAudioSamplesAsync(new byte[] { 3, 4 });
                Assert.That(logs.Count, Is.EqualTo(loggedCount));
                Assert.That(pipeline.RemoteSessionId, Is.EqualTo(sessionId));
                Assert.That(transport.DisposeCount, Is.Zero);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DebugLogsIncludeConversationFieldsWithoutCredentialsMetadataOrAudioPayloads()
        {
            var clock = new FakeClock();
            var transport = new Connection();
            var logs = new ConcurrentQueue<string>();
            const string url = "ws://diagnostic-test.invalid/private-path";
            const string apiKey = "diagnostic-secret-key";
            const string customMetadata = "private-provider-metadata";
            var audio = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
            var encodedAudio = Convert.ToBase64String(audio);
            var pipeline = new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions
            {
                SessionId = "diagnostic-session", Url = url, ApiKey = apiKey,
                Metadata = new JObject { ["custom"] = customMetadata }
            }, () => transport, clock)
            { DebugLogEnabled = true, DebugLog = logs.Enqueue };
            try
            {
                await pipeline.SetAllowBargeInAsync(false);
                await pipeline.ConnectAsync();
                await pipeline.SetAllowBargeInAsync(true);
                await pipeline.SetAllowBargeInAsync(true);
                await pipeline.ProcessAudioSamplesAsync(audio);
                transport.Receive(new JObject { ["type"] = "info", ["metadata"] = new JObject
                { ["partial_request_text"] = "hello\nnext", ["custom"] = customMetadata } });
                var accepted = await ReceiveResponseAsync(pipeline, transport, new JObject
                { ["type"] = "accepted", ["metadata"] = new JObject { ["block_barge_in"] = true } }, SpeechPipelineResponseType.Accepted);
                await ReceiveResponseAsync(pipeline, transport, new JObject
                {
                    ["type"] = "start", ["audio_data"] = encodedAudio,
                    ["metadata"] = new JObject { ["recognized_text"] = "hello recognized", ["custom"] = customMetadata }
                }, SpeechPipelineResponseType.Start);
                await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "final" }, SpeechPipelineResponseType.Final);
                await pipeline.DrainAsync();

                var lines = logs.ToArray();
                Assert.That(Array.Exists(lines, line => line.Contains(" start.send ") && line.Contains("barge_in_enabled=false")), Is.True);
                foreach (var eventName in new[] { "start.sent", "config.send", "config.sent", "config.stored", "config.unchanged" })
                    Assert.That(Array.Exists(lines, line => line.Contains(" " + eventName + " ")), Is.True, eventName);
                foreach (var type in new[] { "accepted", "info", "start", "final" })
                    Assert.That(Array.Exists(lines, line => line.Contains(" receive ") && line.Contains("type=\"" + type + "\"")), Is.True, type);
                Assert.That(Array.Exists(lines, line => line.Contains("partial=\"hello\\nnext\"")), Is.True);
                Assert.That(Array.Exists(lines, line => line.Contains("recognized=\"hello recognized\"")), Is.True);
                Assert.That(Array.Exists(lines, line => line.Contains(" turn.accepted ")
                    && line.Contains("session=\"diagnostic-session\"")
                    && line.Contains("remote=\"" + pipeline.RemoteSessionId + "\"")
                    && line.Contains("transaction=\"" + accepted.TransactionId + "\"")), Is.True);
                var combined = string.Join("\n", lines);
                foreach (var excluded in new[] { url, apiKey, customMetadata, encodedAudio, "audio_data" })
                    Assert.That(combined.Contains(excluded), Is.False, excluded);
                Assert.That(Array.Exists(lines, line => line.Contains("\n") || line.Contains("\r")), Is.False, "Recognition text must remain on one log line.");
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ThrowingDebugObserverDoesNotInterruptTransportOrResponseDelivery()
        {
            var transport = new Connection();
            var observedCount = 0;
            var errors = new ConcurrentQueue<Exception>();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport)
            {
                DebugLogEnabled = true,
                DebugLog = _ => { Interlocked.Increment(ref observedCount); throw new InvalidOperationException("Diagnostic observer failure."); }
            };
            pipeline.Error += errors.Enqueue;
            try
            {
                await pipeline.SetAllowBargeInAsync(false);
                await pipeline.ConnectAsync();
                await pipeline.SetAllowBargeInAsync(true);
                await pipeline.ProcessAudioSamplesAsync(new byte[] { 1, 2 });
                var accepted = await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "accepted" }, SpeechPipelineResponseType.Accepted);
                var final = await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "final" }, SpeechPipelineResponseType.Final);
                await pipeline.DrainAsync();

                Assert.That(Volatile.Read(ref observedCount), Is.GreaterThan(0));
                Assert.That(pipeline.IsConnected, Is.True);
                Assert.That(final.TransactionId, Is.EqualTo(accepted.TransactionId));
                Assert.That(transport.Sent.Exists(message => (string)message["type"] == "config"), Is.True);
                Assert.That(transport.Sent.Exists(message => (string)message["type"] == "data"), Is.True);
                Assert.That(errors.IsEmpty, Is.True);
            }
            finally { await pipeline.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask AudioDebugSummariesAreThrottledWhileAcceptedAndFinalBoundariesRemainVisible()
        {
            var clock = new FakeClock();
            var transport = new Connection();
            var logs = new ConcurrentQueue<string>();
            var pipeline = new AIAvatarSpeechPipelineClient(connectionFactory: () => transport, clock: clock)
            { DebugLogEnabled = true, DebugLog = logs.Enqueue };
            Func<string, int> count = eventName => Array.FindAll(logs.ToArray(), line => line.Contains(" " + eventName + " ")).Length;
            var audio = new byte[] { 1, 2, 3, 4 };
            try
            {
                await pipeline.ConnectAsync();
                for (var index = 0; index < 10; index++) await pipeline.ProcessAudioSamplesAsync(audio);
                Assert.That(count("audio.sent"), Is.EqualTo(1));
                Assert.That(count("audio.after_boundary"), Is.EqualTo(1));
                clock.Advance(0.5);
                await pipeline.ProcessAudioSamplesAsync(audio);
                Assert.That(count("audio.sent"), Is.EqualTo(1));
                clock.Advance(0.5);
                await pipeline.ProcessAudioSamplesAsync(audio);
                Assert.That(count("audio.sent"), Is.EqualTo(2));

                await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "accepted" }, SpeechPipelineResponseType.Accepted);
                await pipeline.ProcessAudioSamplesAsync(audio);
                await pipeline.ProcessAudioSamplesAsync(audio);
                Assert.That(count("audio.after_boundary"), Is.EqualTo(2));
                Assert.That(count("audio.sent"), Is.EqualTo(2));
                await ReceiveResponseAsync(pipeline, transport, new JObject { ["type"] = "final" }, SpeechPipelineResponseType.Final);
                await pipeline.ProcessAudioSamplesAsync(audio);
                Assert.That(count("audio.after_boundary"), Is.EqualTo(3));
                Assert.That(count("audio.sent"), Is.EqualTo(2));
                clock.Advance(1);
                await pipeline.ProcessAudioSamplesAsync(audio);
                Assert.That(count("audio.sent"), Is.EqualTo(3));
                var summaries = Array.FindAll(logs.ToArray(), line => line.Contains(" audio.sent "));
                Assert.That(summaries[2], Does.Contain("sent_frames=16").And.Contain("sent_bytes=64").And.Contain("send_in_flight=False"));
            }
            finally { await pipeline.DisposeAsync(); }
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

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
    public class AIAvatarSpeechPipelineClientLifecycleTests
    {
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

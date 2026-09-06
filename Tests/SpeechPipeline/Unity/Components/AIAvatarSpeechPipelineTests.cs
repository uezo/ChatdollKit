using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.Remote;
using ChatdollKit.SpeechPipeline;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatdollKit.Tests.SpeechPipeline.Unity
{
    public sealed class AIAvatarSpeechPipelineTests
    {
        private GameObject owner;
        private AIAvatarSpeechPipeline component;
        private readonly List<SpeechPipelineLease> leases = new List<SpeechPipelineLease>();
        private readonly List<SpeechCompletionSource<bool>> gates = new List<SpeechCompletionSource<bool>>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            owner = new GameObject("Remote pipeline component test");
            component = owner.AddComponent<AIAvatarSpeechPipeline>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var gate in gates) gate.TrySetResult(true);
            gates.Clear();
            foreach (var lease in leases) yield return Wait(lease.DisposeAsync());
            leases.Clear();
            if (owner != null) UnityEngine.Object.Destroy(owner);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ConnectedLeaseUsesComponentSettingsAndOwnsTheConnection() => Run(async () =>
        {
            var connection = new FakeConnection();
            component.ConnectionFactory = () => connection;
            component.ServerUrl = "ws://offline.invalid:48001/ws";
            component.ApiKey = "offline-test-key";
            component.SessionId = "logical-session";
            component.UserId = "test-user";
            component.ContextId = "test-context";
            component.InputSampleRate = 8000;
            component.SamplesPerMessage = 160;
            var lease = await CreateAsync();
            Assert.That(lease.Pipeline, Is.SameAs(component.Pipeline));
            Assert.That(component.Pipeline.IsConnected, Is.True);
            Assert.That(component.Pipeline.SessionId, Is.EqualTo("logical-session"));
            Assert.That(component.IsBound, Is.True);
            Assert.That(lease.InputSampleRate, Is.EqualTo(8000));
            Assert.That(lease.SamplesPerMessage, Is.EqualTo(160));
            Assert.That(connection.Uri.AbsoluteUri, Is.EqualTo(component.ServerUrl));
            Assert.That(connection.ApiKey, Is.EqualTo("offline-test-key"));
            Assert.That((string)connection.StartMessage["user_id"], Is.EqualTo("test-user"));
            Assert.That((string)connection.StartMessage["context_id"], Is.EqualTo("test-context"));
            await lease.DisposeAsync();
            await lease.DisposeAsync();
            Assert.That(component.IsBound, Is.False);
            Assert.That(component.Pipeline, Is.Null);
            Assert.That(connection.Disposals, Is.EqualTo(1));
            Assert.That(connection.IsOpen, Is.False);
        });

        [UnityTest]
        public IEnumerator InvalidAudioFormatAndPreCanceledStartupNeverCreateAConnection() => Run(async () =>
        {
            var creations = 0;
            component.ConnectionFactory = () => { creations++; return new FakeConnection(); };
            component.InputSampleRate = 0;
            await ExpectAsync<ArgumentOutOfRangeException>(() => component.CreatePipelineAsync(CancellationToken.None));
            component.InputSampleRate = 16000;
            component.SamplesPerMessage = 0;
            await ExpectAsync<ArgumentOutOfRangeException>(() => component.CreatePipelineAsync(CancellationToken.None));
            component.SamplesPerMessage = 512;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await ExpectAsync<OperationCanceledException>(() => component.CreatePipelineAsync(cancellation.Token));
            }
            Assert.That(creations, Is.Zero);
            Assert.That(component.IsBound, Is.False);
        });

        [UnityTest]
        public IEnumerator CancellationDuringConnectDisposesItsLateResultWithoutPublishingBindings() => Run(async () =>
        {
            var gate = Gate();
            var connection = new FakeConnection { ConnectGate = gate };
            component.ConnectionFactory = () => connection;
            using (var cancellation = new CancellationTokenSource())
            {
                var creating = SpeechAsync.Share(component.CreatePipelineAsync(cancellation.Token));
                await connection.ConnectEntered.Task;
                cancellation.Cancel();
                gate.TrySetResult(true);
                await ExpectAsync<OperationCanceledException>(() => creating);
            }
            Assert.That(component.Pipeline, Is.Null);
            Assert.That(component.IsBound, Is.False);
            Assert.That(connection.Disposals, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator ConnectionFailureDisposesItsTransportWithoutPublishingBindings() => Run(async () =>
        {
            var connection = new FakeConnection { ConnectFailure = new InvalidOperationException("offline connect failed") };
            component.ConnectionFactory = () => connection;
            await ExpectAsync<InvalidOperationException>(() => component.CreatePipelineAsync(CancellationToken.None));
            Assert.That(component.Pipeline, Is.Null);
            Assert.That(component.IsBound, Is.False);
            Assert.That(connection.Disposals, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator ConnectionSettingsRequireRestartAndTheNextLeaseUsesTheNewValues() => Run(async () =>
        {
            var connections = new List<FakeConnection>();
            component.ConnectionFactory = () =>
            {
                var connection = new FakeConnection(); connections.Add(connection); return connection;
            };
            var lease = await CreateAsync();
            var original = component.Pipeline;
            component.ServerUrl = "ws://changed.invalid/ws";
            component.InputSampleRate = 8000;
            await component.ApplySettingsAsync();
            Assert.That(component.NeedsRestart, Is.True);
            Assert.That(component.Pipeline, Is.SameAs(original));
            Assert.That(lease.InputSampleRate, Is.EqualTo(16000));
            Assert.That(connections.Count, Is.EqualTo(1));
            await lease.DisposeAsync();
            var next = await CreateAsync();
            Assert.That(component.NeedsRestart, Is.False);
            Assert.That(next.InputSampleRate, Is.EqualTo(8000));
            Assert.That(connections[1].Uri.AbsoluteUri, Is.EqualTo("ws://changed.invalid/ws"));
            Assert.That(connections[0].Disposals, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator LateStartupCannotClearThePipelinePublishedByAnotherStartup() => Run(async () =>
        {
            var gate = Gate();
            var late = new FakeConnection { ConnectGate = gate };
            var winner = new FakeConnection();
            var creations = 0;
            component.ConnectionFactory = () => ++creations == 1 ? late : winner;
            var pending = SpeechAsync.Share(component.CreatePipelineAsync(CancellationToken.None));
            await late.ConnectEntered.Task;
            var lease = await CreateAsync();
            var published = component.Pipeline;
            gate.TrySetResult(true);
            await ExpectAsync<InvalidOperationException>(() => pending);
            Assert.That(component.Pipeline, Is.SameAs(published));
            Assert.That(component.Pipeline, Is.SameAs(lease.Pipeline));
            Assert.That(component.IsBound, Is.True);
            Assert.That(late.Disposals, Is.EqualTo(1));
            Assert.That(winner.Disposals, Is.Zero);
        });

        private async UniTask<SpeechPipelineLease> CreateAsync()
        {
            var lease = await component.CreatePipelineAsync(CancellationToken.None);
            leases.Add(lease);
            return lease;
        }

        private SpeechCompletionSource<bool> Gate()
        {
            var gate = new SpeechCompletionSource<bool>(); gates.Add(gate); return gate;
        }

        private static async UniTask ExpectAsync<T>(Func<UniTask> operation) where T : Exception
        {
            Exception failure = null;
            try { await operation(); } catch (Exception error) { failure = error; }
            Assert.That(failure, Is.InstanceOf<T>());
        }

        private static IEnumerator Run(Func<UniTask> operation) => Wait(operation());
        private static IEnumerator Wait(UniTask task)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, "Remote component operation timed out.");
            task.GetAwaiter().GetResult();
        }

        private sealed class FakeConnection : IAIAvatarConnection
        {
            private readonly Queue<string> messages = new Queue<string>();
            private readonly SpeechAsyncSemaphore available = new SpeechAsyncSemaphore(0);
            public readonly SpeechCompletionSource<bool> ConnectEntered = new SpeechCompletionSource<bool>();
            public SpeechCompletionSource<bool> ConnectGate;
            public Exception ConnectFailure;
            public Uri Uri;
            public string ApiKey;
            public JObject StartMessage;
            public int Disposals;
            public bool IsOpen { get; private set; }

            public async UniTask ConnectAsync(Uri uri, string apiKey, CancellationToken token)
            {
                Uri = uri; ApiKey = apiKey; ConnectEntered.TrySetResult(true);
                if (ConnectGate != null) await ConnectGate.Task;
                token.ThrowIfCancellationRequested();
                if (ConnectFailure != null) throw ConnectFailure;
                IsOpen = true;
            }

            public UniTask SendAsync(string message, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var parsed = JObject.Parse(message);
                if ((string)parsed["type"] == "start")
                {
                    StartMessage = parsed;
                    messages.Enqueue(new JObject
                    {
                        ["type"] = "connected", ["session_id"] = parsed["session_id"], ["context_id"] = parsed["context_id"]
                    }.ToString());
                    available.Release();
                }
                return UniTask.CompletedTask;
            }

            public async UniTask<string> ReceiveAsync(CancellationToken token)
            {
                await available.WaitAsync(token);
                return messages.Count > 0 ? messages.Dequeue() : null;
            }

            public void Abort() { IsOpen = false; available.Release(); }
            public void Dispose()
            {
                Disposals++;
                Abort();
            }
        }
    }
}

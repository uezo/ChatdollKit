using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using ChatdollKit.SpeechPipeline.Remote;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatdollKit.Tests.SpeechPipeline.Unity
{
    /// <summary>Exercises the browser callback boundary without starting a browser or network connection.</summary>
    public sealed class WebGLAIAvatarConnectionTests
    {
        private GameObject owner;
        private AIAvatarWebSocketBridge bridge;
        private WebGLAIAvatarConnection connection;

        [TearDown]
        public void TearDown()
        {
            connection?.Dispose();
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
        }

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("test-key", "Authorization.dGVzdC1rZXk")]
        [TestCase("日本語", "Authorization.5pel5pys6Kqe")]
        [TestCase("\u083e", "Authorization.4KC+")]
        public void AuthorizationUsesTheServersUnpaddedStandardBase64Convention(string key, string expected)
        {
            Assert.That(WebGLAIAvatarConnection.CreateAuthorizationProtocol(key), Is.EqualTo(expected));
        }

        [Test]
        public void AuthorizationRejectsBase64SlashWhichCannotBeAWebSocketProtocolToken()
        {
            Assert.Throws<ArgumentException>(() => WebGLAIAvatarConnection.CreateAuthorizationProtocol("\u083f"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ReceiveBufferMustHaveAPositiveLimit(int limit)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WebGLAIAvatarConnection(limit));
        }

        [UnityTest]
        public IEnumerator MessagesArrivingBeforeTheReceiveContinuationRemainInOrder() => Run(async () =>
        {
            Create();
            var receiving = connection.ReceiveAsync(CancellationToken.None);
            bridge.OnWebSocketMessage("first");
            bridge.OnWebSocketMessage("second");
            bridge.OnWebSocketMessage("third");
            Assert.That(receiving.Status, Is.EqualTo(UniTaskStatus.Pending), "The queued messages must arrive before the first continuation runs.");
            Assert.That(await receiving, Is.EqualTo("first"));
            Assert.That(await connection.ReceiveAsync(CancellationToken.None), Is.EqualTo("second"));
            Assert.That(await connection.ReceiveAsync(CancellationToken.None), Is.EqualTo("third"));
        });

        [UnityTest]
        public IEnumerator ASecondPendingReceiveIsRejectedWithoutReplacingTheFirst() => Run(async () =>
        {
            Create();
            var receiving = connection.ReceiveAsync(CancellationToken.None);
            await ExpectAsync<InvalidOperationException>(() => connection.ReceiveAsync(CancellationToken.None));
            bridge.OnWebSocketMessage("first receiver");
            Assert.That(await receiving, Is.EqualTo("first receiver"));
        });

        [UnityTest]
        public IEnumerator DequeueReleasesTheUtf8ByteBudget() => Run(async () =>
        {
            Create(4);
            bridge.OnWebSocketMessage("Aあ");
            Assert.That(await connection.ReceiveAsync(CancellationToken.None), Is.EqualTo("Aあ"));
            bridge.OnWebSocketMessage("Bい");
            Assert.That(connection.IsOpen, Is.True);
            Assert.That(await connection.ReceiveAsync(CancellationToken.None), Is.EqualTo("Bい"));
        });

        [UnityTest]
        public IEnumerator AMessageOverTheUtf8ByteLimitFailsThePendingReceiver() => Run(async () =>
        {
            Create(4);
            var receiving = connection.ReceiveAsync(CancellationToken.None);
            bridge.OnWebSocketMessage("あい");
            await ExpectAsync<IOException>(() => receiving);
            Assert.That(connection.IsOpen, Is.False);
            bridge.OnWebSocketOpen("");
            bridge.OnWebSocketMessage("late");
            await ExpectAsync<IOException>(() => connection.ReceiveAsync(CancellationToken.None));
            Assert.That(connection.IsOpen, Is.False);
        });

        [UnityTest]
        public IEnumerator AggregateBufferOverflowPreservesAcceptedMessagesThenReportsFailure() => Run(async () =>
        {
            Create(4);
            bridge.OnWebSocketMessage("あ");
            bridge.OnWebSocketMessage("い");
            Assert.That(connection.IsOpen, Is.False);
            Assert.That(await connection.ReceiveAsync(CancellationToken.None), Is.EqualTo("あ"));
            await ExpectAsync<IOException>(() => connection.ReceiveAsync(CancellationToken.None));
        });

        [UnityTest]
        public IEnumerator BrowserFailureCompletesThePendingReceiveAndIgnoresLateCallbacks() => Run(async () =>
        {
            Create();
            var receiving = connection.ReceiveAsync(CancellationToken.None);
            bridge.OnWebSocketError("");
            bridge.OnWebSocketMessage("late");
            bridge.OnWebSocketOpen("");
            bridge.OnWebSocketClose("");
            await ExpectAsync<IOException>(() => receiving);
            await ExpectAsync<IOException>(() => connection.ReceiveAsync(CancellationToken.None));
            Assert.That(connection.IsOpen, Is.False);
        });

        [UnityTest]
        public IEnumerator NormalCloseDrainsAcceptedMessagesThenReturnsEndOfStream() => Run(async () =>
        {
            Create();
            bridge.OnWebSocketMessage("last");
            bridge.OnWebSocketClose("");
            Assert.That(connection.IsOpen, Is.False);
            Assert.That(await connection.ReceiveAsync(CancellationToken.None), Is.EqualTo("last"));
            Assert.That(await connection.ReceiveAsync(CancellationToken.None), Is.Null);
        });

        [UnityTest]
        public IEnumerator AbortCancelsAPendingReceiveAndClearsQueuedMessages() => Run(async () =>
        {
            Create();
            var receiving = connection.ReceiveAsync(CancellationToken.None);
            connection.Abort();
            await ExpectAsync<OperationCanceledException>(() => receiving);
            bridge.OnWebSocketMessage("late");
            await ExpectAsync<OperationCanceledException>(() => connection.ReceiveAsync(CancellationToken.None));
            Assert.That(connection.IsOpen, Is.False);
        });

        [UnityTest]
        public IEnumerator AbortDropsMessagesThatWereQueuedBeforeTheAbort() => Run(async () =>
        {
            Create();
            bridge.OnWebSocketMessage("discard");
            connection.Abort();
            await ExpectAsync<OperationCanceledException>(() => connection.ReceiveAsync(CancellationToken.None));
        });

        [UnityTest]
        public IEnumerator CancelingAPendingReceiveAbortsTheConnection() => Run(async () =>
        {
            Create();
            using (var cancellation = new CancellationTokenSource())
            {
                var receiving = connection.ReceiveAsync(cancellation.Token);
                cancellation.Cancel();
                await ExpectAsync<OperationCanceledException>(() => receiving);
            }
            Assert.That(connection.IsOpen, Is.False);
            bridge.OnWebSocketMessage("late");
            await ExpectAsync<OperationCanceledException>(() => connection.ReceiveAsync(CancellationToken.None));
        });

        [UnityTest]
        public IEnumerator CallbackObjectOnDestroyAbortsThePendingReceive() => Run(async () =>
        {
            Create();
            var receiving = connection.ReceiveAsync(CancellationToken.None);
            // Edit Mode does not invoke OnDestroy for a plain component whose Awake has not run.
            typeof(AIAvatarWebSocketBridge).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bridge, null);
            UnityEngine.Object.DestroyImmediate(owner);
            await ExpectAsync<OperationCanceledException>(() => receiving);
            Assert.That(connection.IsOpen, Is.False);
        });

        [UnityTest]
        public IEnumerator DisposalDetachesCallbacksAndRejectsFurtherReceives() => Run(async () =>
        {
            Create();
            var receiving = connection.ReceiveAsync(CancellationToken.None);
            connection.Dispose();
            await ExpectAsync<OperationCanceledException>(() => receiving);
            connection.Dispose();
            Assert.That(owner == null, Is.True);
            // A SendMessage already in flight may still retain the managed callback target.
            Assert.DoesNotThrow(() => bridge.OnWebSocketOpen(""));
            Assert.DoesNotThrow(() => bridge.OnWebSocketMessage("late"));
            Assert.DoesNotThrow(() => bridge.OnWebSocketError(""));
            Assert.DoesNotThrow(() => bridge.OnWebSocketClose(""));
            Assert.That(connection.IsOpen, Is.False);
            await ExpectAsync<ObjectDisposedException>(() => connection.ReceiveAsync(CancellationToken.None));
        });

        private void Create(int maximumBufferedBytes = 1024)
        {
            connection = new WebGLAIAvatarConnection(maximumBufferedBytes);
            owner = new GameObject("Offline browser WebSocket callback test");
            bridge = owner.AddComponent<AIAvatarWebSocketBridge>();
            // ConnectAsync creates these links only in Web builds. Simulate that setup without importing JavaScript.
            typeof(AIAvatarWebSocketBridge).GetField("Connection", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(bridge, connection);
            typeof(WebGLAIAvatarConnection).GetField("bridge", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(connection, bridge);
            bridge.OnWebSocketOpen("");
            Assert.That(connection.IsOpen, Is.True);
        }

        private static IEnumerator Run(Func<UniTask> operation)
        {
            var task = operation();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!task.Status.IsCompleted() && elapsed.Elapsed < TimeSpan.FromSeconds(5)) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, "Browser callback operation did not resume within five seconds.");
            task.GetAwaiter().GetResult();
        }

        private static async UniTask ExpectAsync<T>(Func<UniTask> operation) where T : Exception
        {
            Exception failure = null;
            try { await operation(); } catch (Exception error) { failure = error; }
            Assert.That(failure, Is.InstanceOf<T>());
        }
    }
}

using System;
using System.Collections;
using System.Threading;
using ChatdollKit.SpeechPipeline.Async;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class SpeechAsyncPlayerLoopTests
    {
        [UnitySetUp]
        public IEnumerator Enter() { yield return new EnterPlayMode(); }

        [UnityTearDown]
        public IEnumerator Exit() { yield return new ExitPlayMode(); }

        [UnityTest]
        public IEnumerator RunYieldAndDelayResumeOnTheMainPlayerLoop()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var entered = false;
            var operation = SpeechAsync.Run(async () =>
            {
                entered = true;
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                await SpeechAsync.Yield();
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                await SpeechAsync.Delay(TimeSpan.FromMilliseconds(20));
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
            });
            Assert.That(entered, Is.False, "Run must defer the callback until the PlayerLoop.");
            yield return Wait(operation);
            Assert.That(entered, Is.True);
        }

        [UnityTest]
        public IEnumerator CompletionDefersEveryAwaiterToThePlayerLoop()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var source = new SpeechCompletionSource<bool>();
            var resumed = 0;
            Action record = () =>
            {
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                resumed++;
            };
            var first = ObserveAsync(source.Task, record);
            var second = ObserveAsync(source.Task, record);
            source.SetResult(true);
            Assert.That(source.Task.Status.IsCompleted(), Is.True);
            Assert.That(resumed, Is.Zero, "Completing a gate must not run callbacks inside the producer.");
            yield return Wait(SpeechAsync.WhenAll(new[] { first, second }));
            Assert.That(resumed, Is.EqualTo(2), "A shared completion must support simultaneous awaiters.");
        }

        [UnityTest]
        public IEnumerator TimeoutCancelsAWaitWithoutLeavingThePlayerLoop()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var source = new SpeechCompletionSource<bool>();
            var canceled = false;
            using (var cancellation = new CancellationTokenSource())
            using (SpeechAsync.Timeout(cancellation, TimeSpan.FromMilliseconds(20)))
            {
                var waiting = ObserveCancellationAsync(source.Task, cancellation.Token, () =>
                {
                    Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                    canceled = true;
                });
                try { yield return Wait(waiting); }
                finally { source.TrySetResult(true); }
                Assert.That(canceled, Is.True);
                Assert.That(cancellation.IsCancellationRequested, Is.True);
            }
        }

        private static async UniTask ObserveAsync(UniTask task, Action completed)
        {
            await task;
            completed();
        }

        private static async UniTask ObserveCancellationAsync(UniTask task, CancellationToken token, Action canceled)
        {
            try { await SpeechAsync.WaitAsync(task, token); Assert.Fail("The wait must time out."); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { canceled(); }
        }

        private static IEnumerator Wait(UniTask task)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 3;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, "The PlayerLoop operation did not complete.");
            task.GetAwaiter().GetResult();
        }
    }
}

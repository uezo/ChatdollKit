using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.Async;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
#if !UNITY_2018_3_OR_NEWER
    [NonParallelizable]
#endif
    public class SpeechAsyncPrimitiveTests
    {
        private ISpeechAsyncScheduler previous;
        private ManualScheduler scheduler;

        [SetUp]
        public void SetUp()
        {
            previous = SpeechAsync.Scheduler;
            SpeechAsync.Scheduler = scheduler = new ManualScheduler();
        }

        [TearDown]
        public void TearDown()
        {
            try { scheduler.Drain(); }
            finally { SpeechAsync.Scheduler = previous; }
        }

        [Test]
        public void CompletionResumesEveryWaiterLaterAndCanBeReadRepeatedly()
        {
            var source = new SpeechCompletionSource<int>();
            var results = new ConcurrentQueue<int>();
            var first = ObserveResult(source.Task, results);
            var second = ObserveResult(source.Task, results);

            source.SetResult(42);
            Assert.That(source.Task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(results, Is.Empty, "Completing a source must not invoke application continuations inline.");
            Assert.That(source.Task.GetAwaiter().GetResult(), Is.EqualTo(42));
            Assert.That(source.Task.GetAwaiter().GetResult(), Is.EqualTo(42));
            scheduler.Drain();
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { 42, 42 }, results);

            var lateCalled = false;
            source.OnCompleted(_ => lateCalled = true, null, 0);
            Assert.That(lateCalled, Is.False);
            scheduler.Drain();
            Assert.That(lateCalled, Is.True);
            Assert.That(source.TrySetResult(43), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FaultAndCancellationReachAllWaitersAndKeepTheFirstOutcome(bool cancel)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var source = new SpeechCompletionSource<int>();
                var first = ObserveFailure(source.Task);
                var second = ObserveFailure(source.Task);
                var failure = new InvalidOperationException("expected failure");
                if (cancel) source.SetCanceled(cancellation.Token);
                else source.SetException(failure);
                Assert.That(source.TrySetResult(99), Is.False);
                scheduler.Drain();

                var firstError = first.GetAwaiter().GetResult();
                var secondError = second.GetAwaiter().GetResult();
                Assert.That(secondError, Is.SameAs(firstError));
                if (cancel)
                    Assert.That(((OperationCanceledException)firstError).CancellationToken, Is.EqualTo(cancellation.Token));
                else Assert.That(firstError, Is.SameAs(failure));
                Assert.Throws<InvalidOperationException>(() => source.SetResult(100));
            }
        }

        [Test]
        public async NUnitTask ConcurrentRegistrationAndCompletionDeliverEachContinuationOnce()
        {
            var source = new SpeechCompletionSource<int>();
            var calls = new int[64];
            using (var start = new ManualResetEventSlim())
            {
                var registrations = Enumerable.Range(0, calls.Length).Select(index => NUnitTask.Run(() =>
                {
                    start.Wait();
                    source.OnCompleted(_ => Interlocked.Increment(ref calls[index]), null, 0);
                })).ToArray();
                var completion = NUnitTask.Run(() => { start.Wait(); source.SetResult(7); });
                start.Set();
                await NUnitTask.WhenAll(registrations.Append(completion));
            }
            Assert.That(calls.All(count => count == 0), Is.True);
            scheduler.Drain();
            Assert.That(calls.All(count => count == 1), Is.True);
            Assert.That(source.Task.GetAwaiter().GetResult(), Is.EqualTo(7));
        }

        [Test]
        public void SemaphoreKeepsFifoOrderWhenTheMiddleWaiterIsCancelled()
        {
            using (var gate = new SpeechAsyncSemaphore(0, 1))
            using (var cancellation = new CancellationTokenSource())
            {
                var first = gate.WaitAsync();
                var canceled = gate.WaitAsync(cancellation.Token);
                var last = gate.WaitAsync();
                cancellation.Cancel();
                Assert.That(canceled.Status, Is.EqualTo(UniTaskStatus.Canceled));
                Assert.That(gate.Release(), Is.Zero);
                Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Succeeded));
                Assert.That(last.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(gate.CurrentCount, Is.Zero);
                gate.Release();
                Assert.That(last.Status, Is.EqualTo(UniTaskStatus.Succeeded));
                Assert.Throws<OperationCanceledException>(() => canceled.GetAwaiter().GetResult());
                first.GetAwaiter().GetResult();
                last.GetAwaiter().GetResult();
                Assert.That(gate.CurrentCount, Is.Zero);
                gate.Release();
                Assert.That(gate.CurrentCount, Is.EqualTo(1));
                Assert.Throws<InvalidOperationException>(() => gate.Release());
            }
        }

        [Test]
        public void SemaphoreReleaseDoesNotRunTheNextOwnersCodeInline()
        {
            using (var gate = new SpeechAsyncSemaphore(0, 1))
            {
                var entered = false;
                var owner = EnterAndRelease(gate, () => entered = true);
                gate.Release();
                Assert.That(entered, Is.False);
                Assert.That(gate.CurrentCount, Is.Zero);
                scheduler.Drain();
                owner.GetAwaiter().GetResult();
                Assert.That(entered, Is.True);
                Assert.That(gate.CurrentCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async NUnitTask CancellationRacingReleaseNeverLosesOrDuplicatesAPermit()
        {
            for (var iteration = 0; iteration < 64; iteration++)
            {
                using (var gate = new SpeechAsyncSemaphore(0, 1))
                using (var cancellation = new CancellationTokenSource())
                {
                    var waiting = gate.WaitAsync(cancellation.Token);
                    await NUnitTask.WhenAll(NUnitTask.Run(cancellation.Cancel), NUnitTask.Run(() => gate.Release()));
                    Assert.That(waiting.Status.IsCompleted(), Is.True);
                    if (waiting.Status == UniTaskStatus.Canceled)
                    {
                        Assert.Throws<OperationCanceledException>(() => waiting.GetAwaiter().GetResult());
                        Assert.That(gate.CurrentCount, Is.EqualTo(1), "A canceled waiter cannot consume the release.");
                        gate.WaitAsync().GetAwaiter().GetResult();
                    }
                    else
                    {
                        waiting.GetAwaiter().GetResult();
                        Assert.That(gate.CurrentCount, Is.Zero, "A successful waiter owns exactly one permit.");
                    }
                    gate.Release();
                    Assert.That(gate.CurrentCount, Is.EqualTo(1));
                    Assert.Throws<InvalidOperationException>(() => gate.Release());
                }
            }
        }

        [Test]
        public void SemaphoreDisposeSettlesEveryWaiterAndRejectsFurtherOperations()
        {
            var gate = new SpeechAsyncSemaphore(0, 1);
            using (var cancellation = new CancellationTokenSource())
            {
                var first = gate.WaitAsync(cancellation.Token);
                var second = gate.WaitAsync();
                var firstFailure = ObserveFailure(first);
                var secondFailure = ObserveFailure(second);
                gate.Dispose();
                cancellation.Cancel();
                scheduler.Drain();
                Assert.That(firstFailure.GetAwaiter().GetResult(), Is.TypeOf<ObjectDisposedException>());
                Assert.That(secondFailure.GetAwaiter().GetResult(), Is.TypeOf<ObjectDisposedException>());
                Assert.Throws<ObjectDisposedException>(() => gate.WaitAsync());
                Assert.Throws<ObjectDisposedException>(() => gate.Release());
                Assert.DoesNotThrow(gate.Dispose);
            }
        }

        [Test]
        public void AlreadyCancelledWaitCannotConsumeAnAvailablePermit()
        {
            using (var gate = new SpeechAsyncSemaphore(1, 1))
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => gate.WaitAsync(cancellation.Token));
                Assert.That(gate.CurrentCount, Is.EqualTo(1));
                gate.WaitAsync().GetAwaiter().GetResult();
                Assert.That(gate.CurrentCount, Is.Zero);
            }
        }

        [Test]
        public void WhenAllWaitsForRemainingWorkAfterFailureAndCancellation()
        {
            var failed = new SpeechCompletionSource<bool>();
            var pending = new SpeechCompletionSource<bool>();
            var canceled = new SpeechCompletionSource<bool>();
            var failure = new InvalidOperationException("first operation failed");
            var settling = ObserveFailure(SpeechAsync.WhenAll(new UniTask[] { failed.Task, pending.Task, canceled.Task }));
            failed.SetException(failure);
            canceled.SetCanceled();
            scheduler.Drain();
            Assert.That(settling.Status, Is.EqualTo(UniTaskStatus.Pending),
                "A failed or canceled operation cannot allow disposal to skip still-running work.");
            pending.SetResult(true);
            scheduler.Drain();
            var result = settling.GetAwaiter().GetResult() as AggregateException;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(result.InnerExceptions[0], Is.SameAs(failure));
            Assert.That(result.InnerExceptions[1], Is.TypeOf<OperationCanceledException>());
        }

        [Test]
        public void DisposedDeadlineCannotCancelItsTargetFromAnAlreadyQueuedExpiry()
        {
            var delay = new SpeechCompletionSource<bool>();
            scheduler.Delay = (duration, token) => delay.Task.AsUniTask();
            using (var target = new CancellationTokenSource())
            {
                var deadline = SpeechAsync.Timeout(target, TimeSpan.FromSeconds(1));
                delay.SetResult(true);
                Assert.That(target.IsCancellationRequested, Is.False, "Timer continuation is still queued.");
                deadline.Dispose();
                scheduler.Drain();
                Assert.That(target.IsCancellationRequested, Is.False,
                    "Disposing a deadline must suppress an expiry that has not been delivered yet.");
            }
        }

        private static async UniTask ObserveResult(UniTask<int> task, ConcurrentQueue<int> results) => results.Enqueue(await task);
        private static async UniTask<Exception> ObserveFailure(UniTask task)
        {
            try { await task; }
            catch (Exception error) { return error; }
            Assert.Fail("Expected an asynchronous failure.");
            return null;
        }
        private static async UniTask EnterAndRelease(SpeechAsyncSemaphore gate, Action entered)
        {
            await gate.WaitAsync();
            entered();
            gate.Release();
        }

        private sealed class ManualScheduler : ISpeechAsyncScheduler
        {
            private readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();
            public Func<TimeSpan, CancellationToken, UniTask> Delay;
            public void Post(Action continuation) => pending.Enqueue(continuation);
            public UniTask DelayAsync(TimeSpan delay, CancellationToken token) => Delay != null
                ? Delay(delay, token) : throw new NotSupportedException("This fixture uses explicit completion gates.");
            public void Drain() { while (pending.TryDequeue(out var continuation)) continuation(); }
        }
    }
}

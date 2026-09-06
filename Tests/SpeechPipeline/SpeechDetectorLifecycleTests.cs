using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;
using ChatdollKit.SpeechPipeline.VAD.TurnEndGates;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class SpeechDetectorLifecycleTests
    {
        private readonly List<SpeechDetectorBase> detectors = new List<SpeechDetectorBase>();
        private readonly List<Action> releases = new List<Action>();
        private readonly List<UniTask> operations = new List<UniTask>();

        [TearDown]
        public async NUnitTask Cleanup()
        {
            foreach (var release in releases) release();
            foreach (var detector in detectors) await Within(detector.DisposeAsync());
            foreach (var operation in operations) await Observe(operation);
            releases.Clear();
            operations.Clear();
            detectors.Clear();
        }

        [Test]
        public async NUnitTask DisposeCancelsMoveNextAndWaitsForEnumeratorDisposal()
        {
            var detector = Own(new ProbeDetector());
            var input = Input();
            var processing = Track(detector.ProcessStreamAsync(input, "stream"));
            await Within(input.WaitingForNext.Task);
            Assert.That(detector.SessionCount, Is.EqualTo(1));

            var disposing = detector.DisposeAsync();
            await Within(input.DisposalStarted.Task);
            Assert.That(input.InputToken.IsCancellationRequested, Is.True);
            Assert.That(disposing.Status.IsCompleted(), Is.False, "Dispose must await the enumerator's asynchronous cleanup.");
            input.ReleaseDisposal();
            await Cancelled(processing);
            await Within(disposing);
            Assert.That(input.DisposeCount, Is.EqualTo(1));
            Assert.That(detector.SessionCount, Is.Zero);
            CollectionAssert.AreEqual(new[] { "stream" }, detector.DeletedSessions.ToArray());
            Assert.That(detector.DisposeAsync(), Is.EqualTo(disposing));
        }

        [Test]
        public async NUnitTask EnumeratorDisposalFailureStillFinalizesTheSession()
        {
            var detector = Own(new ProbeDetector());
            var failure = new InvalidOperationException("enumerator disposal failed");
            var input = Input();
            input.DisposeFailure = failure;
            input.ReleaseDisposal();
            var processing = Track(detector.ProcessStreamAsync(input, "stream"));
            await Within(input.WaitingForNext.Task);
            input.EndInput();
            Assert.That(await Failed<InvalidOperationException>(processing), Is.SameAs(failure));
            Assert.That(input.DisposeCount, Is.EqualTo(1));
            Assert.That(detector.SessionCount, Is.Zero);
            CollectionAssert.AreEqual(new[] { "stream" }, detector.DeletedSessions.ToArray());
        }

        [Test]
        public async NUnitTask SynchronousInfiniteInputReturnsControlAndCanBeDisposed()
        {
            var detector = Own(new ProbeDetector());
            var input = new SynchronousInput();
            releases.Add(() => input.Stop = true);
            var returned = new SpeechCompletionSource<UniTask>();
            // A regression that consumes the entire input before returning must
            // fail within the guard rather than blocking the test runner itself.
            var invocation = Track(SpeechAsync.FromTask(NUnitTask.Run(() =>
                returned.TrySetResult(detector.ProcessStreamAsync(input, "stream")))));
            await Within(returned.Task);
            var processing = Track(returned.Task.GetAwaiter().GetResult());
            await Within(input.Reading.Task);
            await Within(detector.DisposeAsync());
            try { await Within(processing); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { /* Disposal may win the next ProcessSamples entry. */ }
            await Within(invocation);
            Assert.That(input.Token.IsCancellationRequested, Is.True);
            Assert.That(input.DisposeCount, Is.EqualTo(1));
            Assert.That(detector.SessionCount, Is.Zero);
        }

        [Test]
        public async NUnitTask EnumeratorDisposalCannotAwaitDetectorDisposal()
        {
            var detector = Own(new ProbeDetector());
            var input = Input();
            var recover = Signal();
            releases.Add(() => recover.TrySetResult(true));
            input.DisposeHook = async () =>
            {
                var disposing = detector.DisposeAsync();
                // Break a regressed dependency cycle during failed-test cleanup.
                await UniTask.WhenAny(disposing, recover.Task);
            };
            input.ReleaseDisposal();
            var processing = Track(detector.ProcessStreamAsync(input, "stream"));
            await Within(input.WaitingForNext.Task);
            input.EndInput();
            await Failed<InvalidOperationException>(processing);
            Assert.That(input.DisposeCount, Is.EqualTo(1));
            Assert.That(detector.SessionCount, Is.Zero);
            CollectionAssert.AreEqual(new[] { "stream" }, detector.DeletedSessions.ToArray());
        }

        [Test]
        public async NUnitTask EnumeratorCreationFailureRemovesAnExistingSession()
        {
            var detector = Own(new ProbeDetector());
            await detector.ProcessSamplesAsync(Pcm(1000), "stream");
            Assert.That(detector.SessionCount, Is.EqualTo(1));
            var failure = new InvalidOperationException("enumerator creation failed");
            var processing = Track(detector.ProcessStreamAsync(new ThrowingInput(failure), "stream"));
            Assert.That(await Failed<InvalidOperationException>(processing), Is.SameAs(failure));
            Assert.That(detector.SessionCount, Is.Zero);
            CollectionAssert.AreEqual(new[] { "stream" }, detector.DeletedSessions.ToArray());
        }

        [Test]
        public async NUnitTask ThrowingCancellationObserverDoesNotPreventDisposalCleanup()
        {
            var detector = Own(new ProbeDetector());
            var errors = new ConcurrentQueue<Exception>();
            detector.Error += errors.Enqueue;
            var input = Input();
            input.ThrowDuringCancellation = true;
            input.ReleaseDisposal();
            var processing = Track(detector.ProcessStreamAsync(input, "stream"));
            await Within(input.WaitingForNext.Task);
            await Within(detector.DisposeAsync());
            await Cancelled(processing);
            Assert.That(input.CancellationObserved, Is.True);
            Assert.That(input.DisposeCount, Is.EqualTo(1));
            Assert.That(detector.SessionCount, Is.Zero);
            CollectionAssert.AreEqual(new[] { "stream" }, detector.DeletedSessions.ToArray());
            Assert.That(errors.Any(error => error is AggregateException), Is.True);
        }

        [Test]
        public async NUnitTask CancellingQueuedProcessingDoesNotCreateOrMutateItsSession()
        {
            var detector = Own(new ProbeDetector());
            var entered = Signal();
            var unblock = Signal();
            releases.Add(() => unblock.TrySetResult(true));
            detector.ProcessHook = async token =>
            {
                entered.TrySetResult(true);
                using (token.Register(() => unblock.TrySetCanceled())) await unblock.Task;
            };
            var active = Track(detector.ProcessSamplesAsync(Pcm(1000), "active"));
            await Within(entered.Task);
            using (var cancellation = new CancellationTokenSource())
            {
                var queued = Track(detector.ProcessSamplesAsync(Pcm(2000), "queued", cancellation.Token));
                cancellation.Cancel();
                await Cancelled(queued);
                CollectionAssert.AreEqual(new[] { "active" }, detector.CreatedSessions.ToArray());
                CollectionAssert.AreEqual(new[] { "active" }, detector.ProcessedSessions.ToArray());
            }
            unblock.TrySetResult(true);
            await Within(active);
            Assert.That(detector.SessionCount, Is.EqualTo(1));
        }

        [TestCase("process")]
        [TestCase("reset")]
        [TestCase("audio-reset")]
        [TestCase("finalize")]
        [TestCase("drain")]
        [TestCase("dispose")]
        [TestCase("stream")]
        public async NUnitTask ObserverControlReentryFailsWithoutDeadlocking(string operation)
        {
            var detector = Own(new ProbeDetector());
            var errors = new ConcurrentQueue<Exception>();
            var input = Input();
            detector.Error += errors.Enqueue;
            detector.Voiced += id => Control(detector, operation, input, id);
            await Within(detector.ProcessSamplesAsync(Pcm(1000), "session"));
            await Within(detector.DrainAsync());
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors.Single(), Is.TypeOf<InvalidOperationException>());
            Assert.That(detector.SessionCount, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask DetachedSpeechDetectedObserverCannotAwaitItsOwnDrain()
        {
            var detector = Own(new StandardSpeechDetectorEngine(new StandardSpeechDetectorOptions
            {
                MinDuration = 0.1, SilenceDurationThreshold = 0.1
            }));
            var errors = new ConcurrentQueue<Exception>();
            detector.Error += errors.Enqueue;
            detector.SpeechDetected += result => detector.DrainAsync();
            await Feed(detector, 1000, 1000, 0);
            await Within(detector.DrainAsync());
            Assert.That(errors.Single(), Is.TypeOf<InvalidOperationException>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ReentrantGateIsRejectedAndFallsBackToNormalTurnEnd(bool background)
        {
            SileroSpeechDetectorEngine detector = null;
            var gate = new DelegateGate(background, async request =>
            {
                await detector.ResetSessionAsync(request.SessionId);
                return new TurnEndDecision { ShouldEnd = true };
            });
            detector = Own(new SileroSpeechDetectorEngine(new SignalModel(), new SileroSpeechDetectorOptions
            {
                MinDuration = 0.1, SilenceDurationThreshold = 0.1
            }, turnEndGates: new[] { gate }));
            var errors = new ConcurrentQueue<Exception>();
            var finals = new ConcurrentQueue<SpeechDetectionResult>();
            detector.Error += errors.Enqueue;
            detector.SpeechDetected += result => { finals.Enqueue(result); return UniTask.CompletedTask; };
            await Within(Feed(detector, 1000, 1000, 0));
            await Within(detector.DrainAsync());
            if (background) await Within(Feed(detector, 0));
            await Within(detector.DrainAsync());
            Assert.That(errors.Single(), Is.TypeOf<InvalidOperationException>());
            Assert.That(finals.Count, Is.EqualTo(1));
            Assert.That(await detector.IsRecordingAsync(), Is.False);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async NUnitTask ProcessingAndDisposalSettleDuringRecognition(bool partial, bool cancelProcessingFirst)
        {
            var recognizer = new CancellableRecognizer();
            releases.Add(recognizer.Release);
            var detector = Own(new SileroStreamSpeechDetectorEngine(new SignalModel(), recognizer,
                new SileroStreamSpeechDetectorOptions
                {
                    MinDuration = 0.1, SegmentSilenceThreshold = partial ? 0.1 : 10,
                    SilenceDurationThreshold = 0.2
                }));
            var errors = new ConcurrentQueue<Exception>();
            var finals = new ConcurrentQueue<SpeechDetectionResult>();
            detector.Error += errors.Enqueue;
            detector.SpeechDetected += result => { finals.Enqueue(result); return UniTask.CompletedTask; };
            await Feed(detector, 1000, 1000, 0);
            if (partial) await Within(recognizer.Entered.Task);
            using (var cancellation = new CancellationTokenSource())
            {
                var finishing = Track(detector.ProcessSamplesAsync(Pcm(0), cancellationToken: cancellation.Token));
                await Within(recognizer.Entered.Task);
                Assert.That(finishing.Status.IsCompleted(), Is.False);
                if (cancelProcessingFirst)
                {
                    cancellation.Cancel();
                    await Cancelled(finishing);
                    // Partial recognition belongs to the utterance, not the individual input operation.
                    Assert.That(recognizer.Token.IsCancellationRequested, Is.EqualTo(!partial));
                }
                await Within(detector.DisposeAsync());
                await Cancelled(finishing);
                Assert.That(recognizer.Token.IsCancellationRequested, Is.True);
                Assert.That(recognizer.Finished.Task.Status.IsCompleted(), Is.True);
            }
            Assert.That(errors, Is.Empty);
            Assert.That(finals, Is.Empty);
        }

        private T Own<T>(T detector) where T : SpeechDetectorBase
        {
            detectors.Add(detector);
            return detector;
        }

        private ControlledInput Input()
        {
            var input = new ControlledInput();
            releases.Add(() => { input.EndInput(); input.ReleaseDisposal(); });
            return input;
        }

        private UniTask Track(UniTask task) { var shared = SpeechAsync.Share(task); operations.Add(shared); return shared; }

        private static UniTask Control(ProbeDetector detector, string operation, ControlledInput input, string id)
        {
            switch (operation)
            {
                case "process": return detector.ProcessSamplesAsync(Pcm(1000), id);
                case "reset": return detector.ResetSessionAsync(id);
                case "audio-reset": return detector.ResetSessionAudioStateAsync(id);
                case "finalize": return detector.FinalizeSessionAsync(id);
                case "drain": return detector.DrainAsync();
                case "dispose": return detector.DisposeAsync();
                case "stream": return detector.ProcessStreamAsync(input, id);
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private static async UniTask Feed(SpeechDetectorBase detector, params int[] values)
        {
            foreach (var value in values) await detector.ProcessSamplesAsync(Pcm(value));
        }

        private static byte[] Pcm(int value)
        {
            var pcm = new byte[3200];
            for (var i = 0; i < pcm.Length; i += 2)
            {
                pcm[i] = (byte)value;
                pcm[i + 1] = (byte)(value >> 8);
            }
            return pcm;
        }

        private static SpeechCompletionSource<bool> Signal() =>
            new SpeechCompletionSource<bool>();

        private static async UniTask Within(UniTask task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!task.Status.IsCompleted() && DateTime.UtcNow < deadline) await SpeechAsync.Yield();
            Assert.That(task.Status.IsCompleted(), Is.True, "Lifecycle operation timed out.");
            await task;
        }

        private static async UniTask Cancelled(UniTask task)
        {
            await Failed<OperationCanceledException>(task);
        }

        private static async UniTask<T> Failed<T>(UniTask task) where T : Exception
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!task.Status.IsCompleted() && DateTime.UtcNow < deadline) await SpeechAsync.Yield();
            Assert.That(task.Status.IsCompleted(), Is.True, "Expected failure did not settle.");
            try { await task; }
            catch (T error) { return error; }
            Assert.Fail("Expected " + typeof(T).Name);
            return null;
        }

        private static async UniTask Observe(UniTask task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!task.Status.IsCompleted() && DateTime.UtcNow < deadline) await SpeechAsync.Yield();
            Assert.That(task.Status.IsCompleted(), Is.True, "Cleanup left an active operation.");
            try { await task; } catch { /* The test already asserted the expected failure. */ }
        }

        private sealed class ProbeDetector : SpeechDetectorBase
        {
            public readonly ConcurrentQueue<string> CreatedSessions = new ConcurrentQueue<string>();
            public readonly ConcurrentQueue<string> ProcessedSessions = new ConcurrentQueue<string>();
            public readonly ConcurrentQueue<string> DeletedSessions = new ConcurrentQueue<string>();
            public Func<CancellationToken, UniTask> ProcessHook;
            public int SessionCount => SessionsCore.Count();
            public ProbeDetector() : base(new SpeechDetectorOptions()) { }
            protected override RecordingSession CreateSession(string id)
            {
                CreatedSessions.Enqueue(id);
                return new RecordingSession(id);
            }
            protected override async UniTask<bool> ProcessSamplesCoreAsync(byte[] samples, RecordingSession session, CancellationToken token)
            {
                ProcessedSessions.Enqueue(session.SessionId);
                await NotifyVoicedAsync(session.SessionId);
                if (ProcessHook != null) await ProcessHook(token);
                token.ThrowIfCancellationRequested();
                return false;
            }
            protected override void OnSessionDeleted(string sessionId) => DeletedSessions.Enqueue(sessionId);
        }

        private sealed class ControlledInput : IAsyncEnumerable<byte[]>, IAsyncEnumerator<byte[]>
        {
            private readonly SpeechCompletionSource<bool> next = Signal();
            private readonly SpeechCompletionSource<bool> dispose = Signal();
            private CancellationTokenRegistration registration;
            private int moveCount;
            public readonly SpeechCompletionSource<bool> WaitingForNext = Signal();
            public readonly SpeechCompletionSource<bool> DisposalStarted = Signal();
            public CancellationToken InputToken;
            public bool ThrowDuringCancellation;
            public bool CancellationObserved;
            public Exception DisposeFailure;
            public Func<UniTask> DisposeHook;
            public int DisposeCount;
            public byte[] Current => Pcm(1000);
            public IAsyncEnumerator<byte[]> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                InputToken = cancellationToken;
                registration = cancellationToken.Register(() =>
                {
                    CancellationObserved = true;
                    next.TrySetCanceled();
                    if (ThrowDuringCancellation) throw new InvalidOperationException("cancellation observer failed");
                });
                return this;
            }
            public ValueTask<bool> MoveNextAsync()
            {
                if (++moveCount == 1) return new ValueTask<bool>(true);
                WaitingForNext.TrySetResult(true);
                return new ValueTask<bool>(next.Task.AsTask());
            }
            public ValueTask DisposeAsync() => new ValueTask(DisposeCoreAsync().AsTask());
            private async UniTask DisposeCoreAsync()
            {
                DisposeCount++;
                DisposalStarted.TrySetResult(true);
                await dispose.Task;
                registration.Dispose();
                if (DisposeHook != null) await DisposeHook();
                if (DisposeFailure != null) throw DisposeFailure;
            }
            public void EndInput() => next.TrySetResult(false);
            public void ReleaseDisposal() => dispose.TrySetResult(true);
        }

        private sealed class SynchronousInput : IAsyncEnumerable<byte[]>, IAsyncEnumerator<byte[]>
        {
            private readonly byte[] pcm = Pcm(1000);
            private int count;
            public readonly SpeechCompletionSource<bool> Reading = Signal();
            public volatile bool Stop;
            public CancellationToken Token;
            public int DisposeCount;
            public byte[] Current => pcm;
            public IAsyncEnumerator<byte[]> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Token = cancellationToken;
                return this;
            }
            public ValueTask<bool> MoveNextAsync()
            {
                Token.ThrowIfCancellationRequested();
                if (++count >= 2) Reading.TrySetResult(true);
                return new ValueTask<bool>(!Stop);
            }
            public ValueTask DisposeAsync()
            {
                DisposeCount++;
                return default;
            }
        }

        private sealed class ThrowingInput : IAsyncEnumerable<byte[]>
        {
            private readonly Exception failure;
            public ThrowingInput(Exception failure) { this.failure = failure; }
            public IAsyncEnumerator<byte[]> GetAsyncEnumerator(CancellationToken cancellationToken = default) => throw failure;
        }

        private sealed class DelegateGate : TurnEndGateBase
        {
            private readonly bool background;
            private readonly Func<TurnEndRequest, UniTask<TurnEndDecision>> handler;
            public DelegateGate(bool background, Func<TurnEndRequest, UniTask<TurnEndDecision>> handler)
            { this.background = background; this.handler = handler; }
            public override bool RunInBackground => background;
            public override UniTask<TurnEndDecision> ShouldEndTurnAsync(TurnEndRequest request, CancellationToken cancellationToken) => handler(request);
        }

        private sealed class SignalModel : ISileroVadModel
        {
            public UniTask<float> PredictAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(samples[0] == 0 ? 0 : 0.9f);
            }
            public void ResetStates() { }
            public void Dispose() { }
        }

        private sealed class CancellableRecognizer : ISpeechRecognizer
        {
            private readonly SpeechCompletionSource<SpeechRecognitionResult> completion =
                new SpeechCompletionSource<SpeechRecognitionResult>();
            public readonly SpeechCompletionSource<bool> Entered = Signal();
            public readonly SpeechCompletionSource<bool> Finished = Signal();
            public CancellationToken Token;
            public async UniTask<SpeechRecognitionResult> RecognizeAsync(string sessionId, byte[] audio, CancellationToken cancellationToken = default)
            {
                Token = cancellationToken;
                using (cancellationToken.Register(() => completion.TrySetCanceled()))
                {
                    Entered.TrySetResult(true);
                    try { return await completion.Task; }
                    finally { Finished.TrySetResult(true); }
                }
            }
            public void Release() => completion.TrySetResult(new SpeechRecognitionResult());
        }
    }
}

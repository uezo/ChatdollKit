using System;
using System.Collections.Concurrent;
using System.Threading;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.VAD.Silero;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class SileroAsyncInferenceTests
    {
        [Test]
        public async NUnitTask DelayedInferencePreservesInputOrderAndWaitsForTheMatchingProbability()
        {
            var model = new DelayedModel();
            var detector = new SileroSpeechDetectorEngine(model);
            var voiced = 0;
            detector.Voiced += _ => { voiced++; return UniTask.CompletedTask; };
            try
            {
                var first = detector.ProcessSamplesAsync(Pcm(1000));
                var firstRequest = await model.NextAsync();
                var second = detector.ProcessSamplesAsync(Pcm(0));
                await SpeechAsync.Yield();
                Assert.That(model.Calls, Is.EqualTo(1), "The next input must wait until the previous probability is applied.");
                Assert.That(first.Status.IsCompleted(), Is.False);
                Assert.That(second.Status.IsCompleted(), Is.False);
                Assert.That(voiced, Is.Zero);
                Assert.That(firstRequest.Samples[0], Is.EqualTo(1000 / 32768f));

                firstRequest.Completion.TrySetResult(1);
                Assert.That(await Within(first), Is.True);
                var secondRequest = await model.NextAsync();
                Assert.That(secondRequest.Samples[0], Is.Zero);
                secondRequest.Completion.TrySetResult(0);
                Assert.That(await Within(second), Is.True);
                Assert.That(voiced, Is.EqualTo(1));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ResetWaitsForPendingInferenceBeforeResettingModelState()
        {
            var model = new DelayedModel();
            var detector = new SileroSpeechDetectorEngine(model);
            try
            {
                var processing = detector.ProcessSamplesAsync(Pcm(1000));
                var request = await model.NextAsync();
                var resets = model.Resets;
                var resetting = detector.ResetSessionAudioStateAsync();
                await SpeechAsync.Yield();
                Assert.That(resetting.Status.IsCompleted(), Is.False);
                Assert.That(model.Resets, Is.EqualTo(resets));

                request.Completion.TrySetResult(0);
                Assert.That(await Within(processing), Is.False);
                await Within(resetting);
                Assert.That(model.Resets, Is.EqualTo(resets + 1));

                var next = detector.ProcessSamplesAsync(Pcm(2000));
                var nextRequest = await model.NextAsync();
                Assert.That(nextRequest.Samples[0], Is.EqualTo(2000 / 32768f));
                nextRequest.Completion.TrySetResult(1);
                Assert.That(await Within(next), Is.True);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask CanceledInferenceCannotApplyALateSpeechTransition()
        {
            var model = new DelayedModel { HonorCancellation = false };
            var detector = new SileroSpeechDetectorEngine(model, new SileroSpeechDetectorOptions { UseVadIterator = true });
            var voiced = 0;
            var errors = 0;
            detector.Voiced += _ => { voiced++; return UniTask.CompletedTask; };
            detector.Error += _ => errors++;
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    var processing = detector.ProcessSamplesAsync(Pcm(1000), cancellationToken: cancellation.Token);
                    var request = await model.NextAsync();
                    cancellation.Cancel();
                    Assert.That(request.Token.IsCancellationRequested, Is.True);
                    request.Completion.TrySetResult(1);
                    await SpeechAsyncAssert.ThrowsAsync<OperationCanceledException>(async () => await Within(processing));
                    Assert.That(voiced, Is.Zero);
                    Assert.That(errors, Is.Zero, "Cancellation must not become a reported inference error.");
                    Assert.That(await detector.IsRecordingAsync(), Is.False);

                    var next = detector.ProcessSamplesAsync(Pcm(0));
                    var nextRequest = await model.NextAsync();
                    nextRequest.Completion.TrySetResult(0);
                    Assert.That(await Within(next), Is.False, "The canceled frame must not leave the iterator triggered.");
                }
                finally
                {
                    model.ReleasePending();
                    await detector.DisposeAsync();
                }
            }
        }

        [Test]
        public async NUnitTask DisposeCancelsActiveInferenceAndKeepsModelOwnershipWithTheCaller()
        {
            var model = new DelayedModel();
            var detector = new SileroSpeechDetectorEngine(model);
            var errors = 0;
            detector.Error += _ => errors++;
            try
            {
                var processing = detector.ProcessSamplesAsync(Pcm(1000));
                var request = await model.NextAsync();
                var closing = detector.DisposeAsync();
                Assert.That(request.Token.IsCancellationRequested, Is.True);
                await SpeechAsyncAssert.ThrowsAsync<OperationCanceledException>(async () => await Within(processing));
                await Within(closing);
                Assert.That(errors, Is.Zero);
                Assert.That(model.Disposals, Is.Zero);
                await SpeechAsyncAssert.ThrowsAsync<ObjectDisposedException>(async () => await detector.ProcessSamplesAsync(Pcm(1000)));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask AsynchronousInferenceFailureReportsAnErrorAndAllowsTheNextInput()
        {
            var model = new DelayedModel();
            var detector = new SileroSpeechDetectorEngine(model);
            var errors = new ConcurrentQueue<Exception>();
            detector.Error += errors.Enqueue;
            try
            {
                var processing = detector.ProcessSamplesAsync(Pcm(1000));
                var request = await model.NextAsync();
                var failure = new InvalidOperationException("browser inference failed");
                request.Completion.TrySetException(failure);
                Assert.That(await Within(processing), Is.False);
                Assert.That(errors.ToArray(), Is.EqualTo(new[] { failure }));

                var next = detector.ProcessSamplesAsync(Pcm(1000));
                var nextRequest = await model.NextAsync();
                nextRequest.Completion.TrySetResult(1);
                Assert.That(await Within(next), Is.True);
            }
            finally { await detector.DisposeAsync(); }
        }

        private sealed class Prediction
        {
            public float[] Samples;
            public CancellationToken Token;
            public readonly SpeechCompletionSource<float> Completion = new SpeechCompletionSource<float>();
        }

        private sealed class DelayedModel : ISileroVadModel
        {
            private readonly ConcurrentQueue<Prediction> requests = new ConcurrentQueue<Prediction>();
            private readonly ConcurrentBag<Prediction> allRequests = new ConcurrentBag<Prediction>();
            public bool HonorCancellation = true;
            public int Calls, Resets, Disposals;

            public UniTask<float> PredictAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = new Prediction { Samples = (float[])samples.Clone(), Token = cancellationToken };
                allRequests.Add(request);
                Interlocked.Increment(ref Calls);
                requests.Enqueue(request);
                return CompleteAsync(request);
            }

            private async UniTask<float> CompleteAsync(Prediction request)
            {
                if (!HonorCancellation) return await request.Completion.Task;
                using (request.Token.Register(() => request.Completion.TrySetCanceled(request.Token)))
                    return await request.Completion.Task;
            }

            public async UniTask<Prediction> NextAsync()
            {
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < deadline)
                {
                    if (requests.TryDequeue(out var request)) return request;
                    await SpeechAsync.Yield();
                }
                throw new AssertionException("The expected Silero inference did not start.");
            }

            public void ReleasePending()
            {
                foreach (var request in allRequests) request.Completion.TrySetResult(0);
            }
            public void ResetStates() => Interlocked.Increment(ref Resets);
            public void Dispose() => Interlocked.Increment(ref Disposals);
        }

        private static byte[] Pcm(short amplitude)
        {
            var samples = new byte[512 * 2];
            for (var i = 0; i < 512; i++)
            {
                samples[i * 2] = (byte)amplitude;
                samples[i * 2 + 1] = (byte)(amplitude >> 8);
            }
            return samples;
        }

        private static async UniTask<T> Within<T>(UniTask<T> task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!task.Status.IsCompleted() && DateTime.UtcNow < deadline) await SpeechAsync.Yield();
            Assert.That(task.Status.IsCompleted(), Is.True, "Asynchronous Silero operation timed out.");
            return await task;
        }

        private static async UniTask Within(UniTask task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!task.Status.IsCompleted() && DateTime.UtcNow < deadline) await SpeechAsync.Yield();
            Assert.That(task.Status.IsCompleted(), Is.True, "Asynchronous Silero operation timed out.");
            await task;
        }
    }
}

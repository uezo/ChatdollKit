using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD.Silero;
using ChatdollKit.SpeechPipeline.VAD;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Silero
{
    public class SileroStreamPartialLoggingTests
    {
        [Test]
        public async NUnitTask PartialLoggingDoesNotDelayFinalRecognition()
        {
            var owner = new GameObject("Partial logging test");
            var component = owner.AddComponent<SileroStreamSpeechDetector>();
            var context = new QueuedContext();
            var detector = CreateDetector(component, context);
            SetDetector(component, detector);
            var finals = new ConcurrentQueue<SpeechDetectionResult>();
            var errors = new ConcurrentQueue<Exception>();
            detector.SpeechDetected += result => { finals.Enqueue(result); return UniTask.CompletedTask; };
            detector.Error += errors.Enqueue;
            try
            {
                // Keep every UI notification queued while processing PCM through final turn detection.
                await CompleteWithin(Background(async () =>
                {
                    await FeedAsync(detector, true, true, false, false, false);
                    await detector.DrainAsync();
                }));

                Assert.That(errors, Is.Empty);
                Assert.That(finals.Count, Is.EqualTo(1), "Final recognition must not wait for Unity to display the partial log.");
                Assert.That(context.Count, Is.EqualTo(1));
                LogAssert.Expect(LogType.Log, "[STT partial][session-for-test] partial transcript");
                context.Drain();
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                SetDetector(component, null);
                context.Drain();
                await detector.DisposeAsync();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [TestCase("stopped")]
        [TestCase("replaced")]
        [TestCase("disabled")]
        [TestCase("destroyed")]
        [TestCase("logging disabled")]
        public async NUnitTask QueuedPartialLogIsDiscardedWhenItsSourceIsNoLongerCurrent(string change)
        {
            var owner = new GameObject("Partial logging lifecycle test");
            var component = owner.AddComponent<SileroStreamSpeechDetector>();
            var context = new QueuedContext();
            var detector = CreateDetector(component, context);
            SileroStreamSpeechDetectorEngine replacement = null;
            SetDetector(component, detector);
            var errors = new ConcurrentQueue<Exception>();
            detector.Error += errors.Enqueue;
            try
            {
                await CompleteWithin(Background(async () =>
                {
                    await FeedAsync(detector, true, true, false);
                    await detector.DrainAsync();
                }));
                Assert.That(context.Count, Is.EqualTo(1));
                Assert.That(errors, Is.Empty);

                switch (change)
                {
                    case "stopped": SetDetector(component, null); break;
                    case "replaced":
                        replacement = CreateDetector(component, context);
                        SetDetector(component, replacement);
                        break;
                    case "disabled": component.enabled = false; break;
                    case "destroyed": UnityEngine.Object.DestroyImmediate(owner); break;
                    case "logging disabled": component.LogPartialRecognition = false; break;
                }

                var messages = new List<string>();
                Application.LogCallback capture = (message, trace, kind) => messages.Add(message);
                Application.logMessageReceived += capture;
                try { context.Drain(); }
                finally { Application.logMessageReceived -= capture; }
                Assert.That(messages, Is.Empty, "A queued callback must recheck the live component and detector before logging.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (component != null) SetDetector(component, null);
                context.Drain();
                await detector.DisposeAsync();
                if (replacement != null) await replacement.DisposeAsync();
                if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static SileroStreamSpeechDetectorEngine CreateDetector(SileroStreamSpeechDetector component, SynchronizationContext context)
        {
            var previous = SynchronizationContext.Current;
            try
            {
                // Only detector construction captures this context. Test continuations keep Unity's own context.
                SynchronizationContext.SetSynchronizationContext(context);
                var create = typeof(SileroStreamSpeechDetector).GetMethod("CreateDetector", BindingFlags.Instance | BindingFlags.NonPublic);
                return (SileroStreamSpeechDetectorEngine)create.Invoke(component, new object[]
                {
                    new SignalModel(), new DummySpeechRecognizer("partial transcript"),
                    new SileroStreamSpeechDetectorOptions
                    {
                        SampleRate = 16000, ChunkSize = 512, MinDuration = 0.032,
                        SegmentSilenceThreshold = 0.032, SilenceDurationThreshold = 0.096
                    }
                });
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private static void SetDetector(SpeechDetectorComponent component, ISpeechDetector detector)
            => typeof(SpeechDetectorComponent).GetProperty(nameof(SpeechDetectorComponent.Detector)).SetValue(component, detector);

        private static async UniTask FeedAsync(ISpeechDetector detector, params bool[] voiced)
        {
            foreach (var voice in voiced)
            {
                var pcm = new byte[512 * 2];
                if (voice)
                    for (var i = 0; i < pcm.Length; i += 2) { pcm[i] = 0xe8; pcm[i + 1] = 0x03; }
                await detector.ProcessSamplesAsync(pcm, "session-for-test");
            }
        }

        private static async UniTask CompleteWithin(UniTask task)
        {
            task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromSeconds(5), CancellationToken.None)), Is.EqualTo(0), "Recognition unexpectedly waited for the queued Unity callback.");
            await task;
        }

        private sealed class QueuedContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<Action> callbacks = new ConcurrentQueue<Action>();
            public int Count => callbacks.Count;
            public override void Post(SendOrPostCallback callback, object state) => callbacks.Enqueue(() => callback(state));
            public void Drain() { while (callbacks.TryDequeue(out var callback)) callback(); }
        }

        private sealed class SignalModel : ISileroVadModel
        {
            public UniTask<float> PredictAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(samples[0] == 0 ? 0f : 1f);
            }
            public void ResetStates() { }
            public void Dispose() { }
        }
        private static UniTask Background(Func<UniTask> operation)
            => SpeechAsync.Share(SpeechAsync.FromTask(NUnitTask.Run(async () => await operation())));

    }
}

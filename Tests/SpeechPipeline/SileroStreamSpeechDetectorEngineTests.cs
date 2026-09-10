using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;
using ChatdollKit.SpeechPipeline.VAD.TurnEndGates;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class SileroStreamSpeechDetectorEngineTests
    {
        private readonly List<SileroStreamSpeechDetectorEngine> detectors = new List<SileroStreamSpeechDetectorEngine>();
        private readonly List<ControlledRecognizer> recognizers = new List<ControlledRecognizer>();

        [TearDown]
        public async NUnitTask Cleanup()
        {
            // Release deliberately non-cooperative test recognizers even after a failed assertion.
            foreach (var recognizer in recognizers) recognizer.CompleteOutstanding();
            foreach (var detector in detectors) await detector.DisposeAsync();
            detectors.Clear();
            recognizers.Clear();
        }

        [Test]
        public async NUnitTask SpeechStartImmediatelyReportsPcmDurationAndMonotonicTimeWithIsolatedSnapshots()
        {
            var clock = new FakeClock { ElapsedSeconds = 5 };
            var detector = Create(new DummySpeechRecognizer("unused"), clock: clock);
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            var voiced = 0;
            detector.Voiced += _ => { voiced++; return UniTask.CompletedTask; };
            detector.RecognitionUpdated += update =>
            {
                update.RecognitionId = "changed";
                update.IsSpeechActive = false;
                update.AudioDurationSeconds = 99;
                update.ObservedAtSeconds = 999;
            };
            detector.RecognitionUpdated += updates.Enqueue;
            await Feed(detector, "a", 1000);
            Assert.That(await detector.IsRecordingAsync("a"), Is.True);
            Assert.That(voiced, Is.EqualTo(1));
            var started = updates.Single();
            Assert.That(started.Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Started));
            Assert.That(started.RecognitionId, Is.Not.EqualTo("changed"));
            Assert.That(started.IsSpeechActive, Is.True);
            Assert.That(started.AudioDurationSeconds, Is.EqualTo(0.1));
            Assert.That(started.ObservedAtSeconds, Is.EqualTo(5));
            Assert.That(started.Text, Is.Null.Or.Empty);
            clock.ElapsedSeconds = 6;
            await Feed(detector, "a", 1000);
            var activity = updates.Last();
            Assert.That(activity.Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Activity));
            Assert.That(activity.RecognitionId, Is.EqualTo(started.RecognitionId));
            Assert.That(activity.IsSpeechActive, Is.True);
            Assert.That(activity.AudioDurationSeconds, Is.EqualTo(0.1));
            Assert.That(activity.ObservedAtSeconds, Is.EqualTo(6));
            Assert.That(updates.Count(item => item.Kind == SpeechRecognitionUpdateKind.Started), Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask SpeechActivityReportsFalseFramesAndContinuesAfterPartialRecognition()
        {
            var clock = new FakeClock();
            var detector = Create(new DummySpeechRecognizer("already recognized"), clock: clock);
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            detector.RecognitionUpdated += updates.Enqueue;
            await Feed(detector, "a", 1000, 1000);
            clock.ElapsedSeconds = 3;
            await Feed(detector, "a", 0);
            await detector.DrainAsync();
            var partial = updates.Single(item => item.Kind == SpeechRecognitionUpdateKind.Partial);
            var silence = updates.Single(item => item.Kind == SpeechRecognitionUpdateKind.Activity && item.IsSpeechActive == false);
            Assert.That(silence.AudioDurationSeconds, Is.EqualTo(0.1));
            Assert.That(silence.ObservedAtSeconds, Is.EqualTo(3));
            Assert.That(silence.RecognitionId, Is.EqualTo(partial.RecognitionId));
            var beforeResume = updates.Count;
            clock.ElapsedSeconds = 4;
            await Feed(detector, "a", 1000);
            var resumed = updates.Last();
            Assert.That(updates.Count, Is.EqualTo(beforeResume + 1));
            Assert.That(resumed.Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Activity));
            Assert.That(resumed.IsSpeechActive, Is.True);
            Assert.That(resumed.RecognitionId, Is.EqualTo(partial.RecognitionId));
            Assert.That(resumed.ObservedAtSeconds, Is.EqualTo(4));
            clock.ElapsedSeconds = 5;
            await Feed(detector, "a", 0);
            Assert.That(updates.Any(item => item.Kind == SpeechRecognitionUpdateKind.Activity &&
                item.IsSpeechActive == false && item.ObservedAtSeconds == 5), Is.True);
            Assert.That(updates.Count(item => item.Kind == SpeechRecognitionUpdateKind.Started), Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask ShortSpeechConfirmsAndStopsActivityUntilTheNextRecording()
        {
            var detector = Create(new DummySpeechRecognizer("short phrase"), FinalOnlyOptions());
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            detector.RecognitionUpdated += updates.Enqueue;
            var finals = CaptureFinals(detector);
            await Feed(detector, "a", 1000, 1000, 0, 0);
            await detector.DrainAsync();
            var confirmed = updates.Last();
            Assert.That(confirmed.Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Confirmed));
            Assert.That(confirmed.Text, Is.EqualTo("short phrase"));
            Assert.That(finals.Single().RecordedDuration, Is.EqualTo(0.2).Within(1e-8));
            Assert.That(finals.Single().RecognitionId, Is.EqualTo(confirmed.RecognitionId));
            var terminalCount = updates.Count;
            await Feed(detector, "a", 0, 0);
            Assert.That(updates.Count, Is.EqualTo(terminalCount));
            await Feed(detector, "a", 1000);
            Assert.That(updates.Last().Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Started));
            Assert.That(updates.Last().RecognitionId, Is.Not.EqualTo(confirmed.RecognitionId));
        }

        [Test]
        public async NUnitTask SpeechActivityDoesNotContinueAfterFailedRecognitionEvenIfRecordingRemainsActive()
        {
            var detector = Create(new DummySpeechRecognizer("recognized"), FinalOnlyOptions());
            detector.ValidateRecognizedText = _ => throw new InvalidOperationException("validation failed");
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            detector.RecognitionUpdated += updates.Enqueue;
            await Feed(detector, "a", 1000, 1000, 0);
            await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(() => Feed(detector, "a", 0));
            Assert.That(updates.Last().Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Canceled));
            Assert.That(await detector.IsRecordingAsync("a"), Is.True, "A common recognition close does not change legacy recording state.");
            var terminalCount = updates.Count;
            await Feed(detector, "a", 1000);
            Assert.That(updates.Count, Is.EqualTo(terminalCount));
            await detector.ResetSessionAudioStateAsync("a");
            await Feed(detector, "a", 1000);
            Assert.That(updates.Last().Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Started));
        }

        [Test]
        public async NUnitTask DiscardedShortRecordingPublishesItsFalseActivityAndThenCloses()
        {
            var options = FinalOnlyOptions();
            options.MinDuration = 0.5;
            var recognizer = Controlled();
            var detector = Create(recognizer, options);
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            detector.RecognitionUpdated += updates.Enqueue;
            await Feed(detector, "a", 1000, 0, 0);
            Assert.That(recognizer.Count, Is.Zero);
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Activity,
                SpeechRecognitionUpdateKind.Activity, SpeechRecognitionUpdateKind.Canceled }));
            Assert.That(updates.Where(item => item.Kind == SpeechRecognitionUpdateKind.Activity)
                .All(item => item.IsSpeechActive == false && item.AudioDurationSeconds == 0.1), Is.True);
            Assert.That(updates.Select(item => item.RecognitionId).Distinct().Count(), Is.EqualTo(1));
            await Feed(detector, "a", 0, 0);
            Assert.That(updates.Count, Is.EqualTo(4));
        }

        [Test]
        public async NUnitTask CommonRecognitionClosesSameIdAndCorrelatesFinalDetection()
        {
            var detector = Create(new DummySpeechRecognizer("recognized"));
            var updates = CaptureRecognitionState(detector);
            var thresholdStarts = 0;
            detector.RecordingStarted += _ => { thresholdStarts++; return UniTask.CompletedTask; };
            var finals = CaptureFinals(detector);
            await Feed(detector, "a", 1000);
            Assert.That(updates.Single().Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Started));
            Assert.That(updates.Single().Text, Is.Null.Or.Empty);
            Assert.That(thresholdStarts, Is.Zero, "Speech onset must not wait for the existing RecordingStarted threshold.");
            await Feed(detector, "a", 1000, 0);
            await detector.DrainAsync();
            await Feed(detector, "a", 0, 0, 0, 0);
            await detector.DrainAsync();
            var actual = updates.ToArray();
            Assert.That(actual.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Partial, SpeechRecognitionUpdateKind.Confirmed }));
            Assert.That(actual.Select(item => item.RecognitionId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(finals.Single().RecognitionId, Is.EqualTo(actual[0].RecognitionId));
            Assert.That(actual[2].Text, Is.EqualTo("recognized"));
        }

        [TestCase("reset")]
        [TestCase("mute")]
        [TestCase("dispose")]
        public async NUnitTask CommonRecognitionCancelsSpeechOnsetWithoutAnyTranscript(string boundary)
        {
            var detector = Create(new DummySpeechRecognizer("unused"));
            var updates = CaptureRecognitionState(detector);
            await Feed(detector, "a", 1000, 1000);
            Assert.That(updates.Single().Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Started));
            if (boundary == "reset") await detector.ResetSessionAudioStateAsync("a");
            else if (boundary == "mute")
            {
                detector.ShouldMute = () => true;
                await Feed(detector, "a", 1000);
            }
            else await detector.DisposeAsync();
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Canceled }));
            Assert.That(updates.Last().RecognitionId, Is.EqualTo(updates.First().RecognitionId));
        }

        [Test]
        public async NUnitTask CommonRecognitionCancelsWhenRecordingIsTooShort()
        {
            var options = FinalOnlyOptions();
            options.MinDuration = 0.5;
            var recognizer = Controlled();
            var detector = Create(recognizer, options);
            var updates = CaptureRecognitionState(detector);
            await Feed(detector, "a", 1000, 0, 0);
            Assert.That(recognizer.Count, Is.Zero);
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Canceled }));
            Assert.That(updates.Last().RecognitionId, Is.EqualTo(updates.First().RecognitionId));
        }

        [Test]
        public async NUnitTask CommonRecognitionDoesNotPublishOldSequenceAfterNormalReset()
        {
            var recognizer = Controlled();
            var detector = Create(recognizer);
            var updates = CaptureRecognitionState(detector);
            await Feed(detector, "a", 1000, 1000, 0);
            var old = await recognizer.RequestAsync(0);
            await detector.ResetSessionAsync("a");
            await Feed(detector, "a", 2000, 2000, 0);
            var current = await recognizer.RequestAsync(1);
            old.Complete("old turn");
            current.Complete("current turn");
            await detector.DrainAsync();
            Assert.That(updates.Where(item => item.Kind == SpeechRecognitionUpdateKind.Partial).Select(item => item.Text), Is.EqualTo(new[] { "current turn" }));
            await detector.ResetSessionAudioStateAsync("a");
            Assert.That(updates.Last().Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Canceled));
            Assert.That(updates.Last().RecognitionId, Is.EqualTo(updates.Single(item => item.Kind == SpeechRecognitionUpdateKind.Partial).RecognitionId));
        }

        [Test]
        public async NUnitTask PartialAndFinalUseTheSameCumulativeAudioIncludingDuplicatedOnset()
        {
            var inputs = new ConcurrentQueue<byte[]>();
            var detector = Create(new DummySpeechRecognizer(handler: (id, audio, token) =>
            {
                inputs.Enqueue(audio);
                return UniTask.FromResult(new SpeechRecognitionResult { Text = "recognized" });
            }), new SileroStreamSpeechDetectorOptions
            {
                SegmentSilenceThreshold = 0.1, SilenceDurationThreshold = 0.1,
                MinDuration = 0.1, PrerollBufferCount = 2
            });
            var results = CaptureFinals(detector);
            await Feed(detector, "a", 0, 1000, 2000, 0);
            await detector.DrainAsync();

            Assert.That(inputs.Count, Is.EqualTo(1));
            Assert.That(results.Count, Is.EqualTo(1));
            var expected = new[] { 0, 1000, 1000, 2000, 0 }.SelectMany(Pcm).ToArray();
            CollectionAssert.AreEqual(expected, inputs.Single());
            CollectionAssert.AreEqual(inputs.Single(), results.Single().Audio);
            Assert.That(results.Single().RecordedDuration, Is.EqualTo(0.2).Within(1e-8));
        }

        [Test]
        public async NUnitTask RuntimeSegmentSilenceUpdateTriggersPartialRecognitionInCurrentRecording()
        {
            var recognizer = Controlled();
            var detector = Create(recognizer, new SileroStreamSpeechDetectorOptions
            {
                SegmentSilenceThreshold = 0.5, SilenceDurationThreshold = 1,
                MinDuration = 0.1
            });
            var partials = new ConcurrentQueue<string>();
            detector.SpeechDetecting += (text, session) => { partials.Enqueue(text); return UniTask.CompletedTask; };
            await Feed(detector, "a", 1000, 1000, 0);
            Assert.That(recognizer.Count, Is.Zero);

            var next = (SileroStreamSpeechDetectorOptions)detector.GetOptions();
            next.SegmentSilenceThreshold = 0.2;
            await detector.UpdateRuntimeOptionsAsync(next);
            await Feed(detector, "a", 0);
            var request = await recognizer.RequestAsync(0);
            Assert.That(await detector.IsRecordingAsync("a"), Is.True);
            request.Complete("partial after settings change");
            await detector.DrainAsync();
            CollectionAssert.AreEqual(new[] { "partial after settings change" }, partials);
            CollectionAssert.AreEqual(new[] { 1000, 1000, 1000, 0, 0 }.SelectMany(Pcm).ToArray(), request.Audio);
        }

        [Test]
        public async NUnitTask ALongPauseStartsOneRequestAndOnlyNewestSequencePublishes()
        {
            var recognizer = Controlled();
            var detector = Create(recognizer);
            var partials = new ConcurrentQueue<string>();
            var latestReceived = NewSignal();
            detector.SpeechDetecting += (text, session) =>
            {
                partials.Enqueue(text);
                latestReceived.TrySetResult(true);
                return UniTask.CompletedTask;
            };
            await Feed(detector, "a", 1000, 1000, 0);
            var first = await recognizer.RequestAsync(0);
            await Feed(detector, "a", 0, 0);
            Assert.That(recognizer.Count, Is.EqualTo(1));
            await Feed(detector, "a", 2000, 0);
            var latest = await recognizer.RequestAsync(1);
            Assert.That(latest.Audio.Length, Is.GreaterThan(first.Audio.Length));
            CollectionAssert.AreEqual(first.Audio, latest.Audio.Take(first.Audio.Length).ToArray());
            latest.Complete("newest");
            await Within(latestReceived.Task);
            first.Complete("older");
            await detector.DrainAsync();
            CollectionAssert.AreEqual(new[] { "newest" }, partials.ToArray());
        }

        [Test]
        public async NUnitTask NormalResetRetainsTheUpstreamLateResultSequenceQuirk()
        {
            var recognizer = Controlled();
            var detector = Create(recognizer);
            var partials = new ConcurrentQueue<string>();
            var sequences = new ConcurrentQueue<int>();
            var oldReceived = NewSignal();
            detector.SpeechDetecting += (text, session) =>
            {
                partials.Enqueue(text);
                sequences.Enqueue(session.RecognitionSequence);
                if (text == "old turn") oldReceived.TrySetResult(true);
                return UniTask.CompletedTask;
            };
            await Feed(detector, "a", 1000, 1000, 0);
            var old = await recognizer.RequestAsync(0);
            await detector.ResetSessionAsync("a");
            Assert.That(old.Token.IsCancellationRequested, Is.False);
            await Feed(detector, "a", 2000, 2000, 0);
            var current = await recognizer.RequestAsync(1);
            old.Complete("old turn");
            await Within(oldReceived.Task);
            current.Complete("current turn");
            await detector.DrainAsync();
            CollectionAssert.AreEqual(new[] { "old turn", "current turn" }, partials.ToArray());
            CollectionAssert.AreEqual(new[] { 1, 1 }, sequences.ToArray());
        }

        [Test]
        public async NUnitTask ExplicitAudioResetCancelsEveryRecognitionAndSuppressesLateResults()
        {
            var recognizer = Controlled();
            var detector = Create(recognizer);
            var partials = new ConcurrentQueue<string>();
            detector.SpeechDetecting += (text, session) => { partials.Enqueue(text); return UniTask.CompletedTask; };
            await Feed(detector, "a", 1000, 1000, 0);
            var first = await recognizer.RequestAsync(0);
            await Feed(detector, "a", 2000, 0);
            var latest = await recognizer.RequestAsync(1);
            await detector.ResetSessionAudioStateAsync("a");
            Assert.That(first.Token.IsCancellationRequested, Is.True);
            Assert.That(latest.Token.IsCancellationRequested, Is.True);
            await Feed(detector, "a", 3000, 3000, 0);
            var current = await recognizer.RequestAsync(2);
            latest.Complete("cancelled");
            first.Complete("old sequence one");
            current.Complete("current");
            await Within(detector.DrainAsync());
            CollectionAssert.AreEqual(new[] { "current" }, partials.ToArray());
        }

        [Test]
        public async NUnitTask ExplicitAudioResetCancelsOverlappingRecognitionsAndAllowsDrainToFinish()
        {
            var recognizer = Controlled(honorCancellation: true);
            var detector = Create(recognizer);
            await Feed(detector, "a", 1000, 1000, 0);
            var first = await recognizer.RequestAsync(0);
            await Feed(detector, "a", 2000, 0);
            var latest = await recognizer.RequestAsync(1);
            var drain = detector.DrainAsync();
            Assert.That(drain.Status.IsCompleted(), Is.False);

            await Within(detector.ResetSessionAudioStateAsync("a"));
            await Within(drain);

            Assert.That(first.Token.IsCancellationRequested, Is.True);
            Assert.That(latest.Token.IsCancellationRequested, Is.True);
            Assert.That(first.Completion.Task.Status.IsCanceled(), Is.True);
            Assert.That(latest.Completion.Task.Status.IsCanceled(), Is.True);
            Assert.That(await detector.IsRecordingAsync("a"), Is.False);
        }

        [Test]
        public async NUnitTask NormalResetKeepsAllRecognitionsRunningUntilExplicitAudioReset()
        {
            var recognizer = Controlled(honorCancellation: true);
            var detector = Create(recognizer);
            await Feed(detector, "a", 1000, 1000, 0);
            var first = await recognizer.RequestAsync(0);
            await Feed(detector, "a", 2000, 0);
            var second = await recognizer.RequestAsync(1);

            await detector.ResetSessionAsync("a");
            Assert.That(first.Token.IsCancellationRequested, Is.False);
            Assert.That(second.Token.IsCancellationRequested, Is.False);
            Assert.That(first.Completion.Task.Status.IsCompleted(), Is.False);
            Assert.That(second.Completion.Task.Status.IsCompleted(), Is.False);

            await Feed(detector, "a", 3000, 3000, 0);
            var current = await recognizer.RequestAsync(2);
            await Within(detector.ResetSessionAudioStateAsync("a"));
            await Within(detector.DrainAsync());
            Assert.That(first.Token.IsCancellationRequested, Is.True);
            Assert.That(second.Token.IsCancellationRequested, Is.True);
            Assert.That(current.Token.IsCancellationRequested, Is.True);
        }

        [Test]
        public async NUnitTask ExplicitAudioResetLeavesOtherSessionsRecognitionRunning()
        {
            var recognizer = Controlled(honorCancellation: true);
            var detector = Create(recognizer);
            await Feed(detector, "a", 1000, 1000, 0);
            var first = await recognizer.RequestAsync(0);
            await Feed(detector, "a", 2000, 0);
            var second = await recognizer.RequestAsync(1);
            await Feed(detector, "b", 3000, 3000, 0);
            var other = await recognizer.RequestAsync(2);

            await Within(detector.ResetSessionAudioStateAsync("a"));
            Assert.That(first.Token.IsCancellationRequested, Is.True);
            Assert.That(second.Token.IsCancellationRequested, Is.True);
            Assert.That(other.Token.IsCancellationRequested, Is.False);
            Assert.That(other.Completion.Task.Status.IsCompleted(), Is.False);
            other.Complete("other session");
            await Within(detector.DrainAsync());
        }

        [Test]
        public async NUnitTask ExplicitAudioResetContinuesWhenACancellationCallbackThrows()
        {
            var recognizer = Controlled(honorCancellation: true);
            var detector = Create(recognizer);
            var errors = new ConcurrentQueue<Exception>();
            detector.Error += errors.Enqueue;
            await Feed(detector, "a", 1000, 1000, 0);
            var first = await recognizer.RequestAsync(0);
            await Feed(detector, "a", 2000, 0);
            var second = await recognizer.RequestAsync(1);
            var failure = new InvalidOperationException("Cancellation observer failed.");
            using (first.Token.Register(() => throw failure))
            {
                await Within(detector.ResetSessionAudioStateAsync("a"));
                await Within(detector.DrainAsync());
            }
            Assert.That(first.Token.IsCancellationRequested, Is.True);
            Assert.That(second.Token.IsCancellationRequested, Is.True);
            Assert.That(errors.Single(), Is.TypeOf<AggregateException>());
            Assert.That(((AggregateException)errors.Single()).InnerExceptions, Does.Contain(failure));
            Assert.That(await detector.IsRecordingAsync("a"), Is.False);
        }

        [Test]
        public async NUnitTask SilenceCompletionRunsFallbackRecognitionWhenNoPartialWasStarted()
        {
            var calls = 0;
            var detector = Create(new DummySpeechRecognizer(handler: (id, audio, token) =>
            {
                calls++;
                return UniTask.FromResult(new SpeechRecognitionResult { Text = "fallback" });
            }), FinalOnlyOptions());
            var results = CaptureFinals(detector);
            await Feed(detector, "a", 1000, 1000, 0, 0);
            await detector.DrainAsync();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(results.Single().Text, Is.EqualTo("fallback"));
        }

        [Test]
        public async NUnitTask MaximumDurationWithoutPartialTextDropsAudioWithoutFallback()
        {
            var calls = 0;
            var options = FinalOnlyOptions();
            options.MaxDuration = 0.3;
            var detector = Create(new DummySpeechRecognizer(handler: (id, audio, token) =>
            {
                calls++;
                return UniTask.FromResult(new SpeechRecognitionResult { Text = "must not be used" });
            }), options);
            var results = CaptureFinals(detector);
            var updates = CaptureRecognitionState(detector);
            await Feed(detector, "a", 1000, 1000, 1000);
            await detector.DrainAsync();
            Assert.That(calls, Is.Zero);
            Assert.That(results, Is.Empty);
            Assert.That(await detector.IsRecordingAsync("a"), Is.False);
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Canceled }));
            Assert.That(updates.Last().RecognitionId, Is.EqualTo(updates.First().RecognitionId));
        }

        [Test]
        public async NUnitTask MaximumDurationReusesLastPartialText()
        {
            var options = DefaultOptions();
            options.MaxDuration = 0.5;
            var detector = Create(new DummySpeechRecognizer("partial"), options);
            var results = CaptureFinals(detector);
            await Feed(detector, "a", 1000, 1000, 0);
            await detector.DrainAsync();
            await Feed(detector, "a", 2000, 2000);
            await detector.DrainAsync();
            Assert.That(results.Single().Text, Is.EqualTo("partial"));
            Assert.That(results.Single().RecordedDuration, Is.EqualTo(0.5).Within(1e-8));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("reject", true)]
        public async NUnitTask EmptyOrRejectedFinalTextDoesNotPublish(string text, bool reject)
        {
            var detector = Create(new DummySpeechRecognizer(text), FinalOnlyOptions());
            if (reject) detector.ValidateRecognizedText = _ => "rejected";
            var updates = CaptureRecognitionState(detector);
            var results = CaptureFinals(detector);
            await Feed(detector, "a", 1000, 1000, 0, 0);
            await detector.DrainAsync();
            Assert.That(results, Is.Empty);
            Assert.That(await detector.IsRecordingAsync("a"), Is.False);
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Canceled }));
            Assert.That(updates.Last().RecognitionId, Is.EqualTo(updates.First().RecognitionId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask RecognitionErrorsReachTheDedicatedCallback(bool finalOnly)
        {
            var failure = new InvalidOperationException("recognizer failure");
            var detector = Create(new DummySpeechRecognizer(handler: (id, audio, token) =>
                UniTask.FromException<SpeechRecognitionResult>(failure)), finalOnly ? FinalOnlyOptions() : DefaultOptions());
            var errors = new ConcurrentQueue<Exception>();
            var updates = CaptureRecognitionState(detector);
            detector.SpeechRecognitionError += (error, id) => { errors.Enqueue(error); return UniTask.CompletedTask; };
            await Feed(detector, "a", 1000, 1000, 0);
            if (finalOnly) await Feed(detector, "a", 0);
            await detector.DrainAsync();
            Assert.That(errors.Single(), Is.SameAs(failure));
            if (finalOnly)
            {
                Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
                { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Canceled }));
                Assert.That(updates.Last().RecognitionId, Is.EqualTo(updates.First().RecognitionId));
            }
        }

        [Test]
        public async NUnitTask CommonRecognitionCancelsWhenFinalValidationThrows()
        {
            var detector = Create(new DummySpeechRecognizer("recognized"), FinalOnlyOptions());
            detector.ValidateRecognizedText = _ => throw new InvalidOperationException("validation failed");
            var updates = CaptureRecognitionState(detector);
            await Feed(detector, "a", 1000, 1000, 0);
            await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(() => Feed(detector, "a", 0));
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Canceled }));
            Assert.That(updates.Last().RecognitionId, Is.EqualTo(updates.First().RecognitionId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask RecognizerReentryFailsWithoutDeadlocking(bool finalOnly)
        {
            SileroStreamSpeechDetectorEngine detector = null;
            var recognizer = new DummySpeechRecognizer(handler: async (id, audio, token) =>
            {
                await detector.ResetSessionAsync(id);
                return new SpeechRecognitionResult { Text = "unreachable" };
            });
            detector = Create(recognizer, finalOnly ? FinalOnlyOptions() : DefaultOptions());
            var errors = new ConcurrentQueue<Exception>();
            detector.SpeechRecognitionError += (error, id) => { errors.Enqueue(error); return UniTask.CompletedTask; };
            await Within(Feed(detector, "a", 1000, 1000, 0));
            if (finalOnly) await Within(Feed(detector, "a", 0));
            await Within(detector.DrainAsync());
            Assert.That(errors.Single(), Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async NUnitTask SessionRecognizerOverrideSurvivesResetAndDoesNotAffectOtherSessions()
        {
            var defaultRecognizer = new DummySpeechRecognizer("default");
            var sessionRecognizer = new DummySpeechRecognizer("override");
            var detector = Create(defaultRecognizer, FinalOnlyOptions());
            Assert.That(await detector.GetSpeechRecognizerAsync("missing"), Is.SameAs(defaultRecognizer));
            await detector.SetSessionDataAsync("missing", "probe", 1);
            Assert.That(await detector.GetSessionDataAsync("missing", "probe"), Is.Null);
            await detector.SetSpeechRecognizerAsync("a", sessionRecognizer);
            await detector.ResetSessionAsync("a");
            Assert.That(await detector.GetSpeechRecognizerAsync("a"), Is.SameAs(sessionRecognizer));
            var results = CaptureFinals(detector);
            await Feed(detector, "a", 1000, 1000, 0, 0);
            await Feed(detector, "b", 1000, 1000, 0, 0);
            await detector.DrainAsync();
            Assert.That(results.Single(result => result.SessionId == "a").Text, Is.EqualTo("override"));
            Assert.That(results.Single(result => result.SessionId == "b").Text, Is.EqualTo("default"));
            await detector.ClearSpeechRecognizerAsync("a");
            Assert.That(await detector.GetSpeechRecognizerAsync("a"), Is.SameAs(defaultRecognizer));
        }

        [Test]
        public async NUnitTask TurnEndGateWaitsForPartialRecognitionAndReceivesItsText()
        {
            var recognizer = Controlled();
            var gate = new CapturingGate();
            var options = DefaultOptions();
            options.SilenceDurationThreshold = 0.2;
            var detector = Create(recognizer, options, new[] { gate });
            var results = CaptureFinals(detector);
            await Feed(detector, "a", 1000, 1000, 0);
            var pending = await recognizer.RequestAsync(0);
            var finishing = detector.ProcessSamplesAsync(Pcm(0), "a");
            Assert.That(finishing.Status.IsCompleted(), Is.False);
            Assert.That(gate.Requests, Is.Empty);
            pending.Complete("gate text");
            await Within(finishing);
            await detector.DrainAsync();
            Assert.That(gate.Requests.Single().Text, Is.EqualTo("gate text"));
            Assert.That(results.Single().Text, Is.EqualTo("gate text"));
        }

        [Test]
        public async NUnitTask RecognizedTextCanTriggerRecordingStartedOnce()
        {
            var detector = Create(new DummySpeechRecognizer("hello"));
            var starts = new ConcurrentQueue<string>();
            detector.RecordingStarted += id => { starts.Enqueue(id); return UniTask.CompletedTask; };
            await Feed(detector, "a", 1000, 1000, 0);
            await detector.DrainAsync();
            await Feed(detector, "a", 1000, 0);
            await detector.DrainAsync();
            CollectionAssert.AreEqual(new[] { "a" }, starts.ToArray());
        }

        [Test]
        public async NUnitTask FinalizeDiscardsUnfinishedAudioWithoutCallingRecognizer()
        {
            var calls = 0;
            var detector = Create(new DummySpeechRecognizer(handler: (id, audio, token) =>
            {
                calls++;
                return UniTask.FromResult(new SpeechRecognitionResult { Text = "unexpected" });
            }));
            var results = CaptureFinals(detector);
            var updates = CaptureRecognitionState(detector);
            await Feed(detector, "a", 1000, 1000);
            await detector.FinalizeSessionAsync("a");
            await detector.FinalizeSessionAsync("a");
            await detector.DrainAsync();
            Assert.That(calls, Is.Zero);
            Assert.That(results, Is.Empty);
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Canceled }));
            Assert.That(updates.Last().RecognitionId, Is.EqualTo(updates.First().RecognitionId));
        }

        [Test]
        public async NUnitTask FinalRecognitionPerformanceUsesTheInjectedClock()
        {
            var clock = new FakeClock();
            var detector = Create(new DummySpeechRecognizer(handler: (id, audio, token) =>
            {
                clock.ElapsedSeconds += 0.25;
                return UniTask.FromResult(new SpeechRecognitionResult { Text = "timed" });
            }), FinalOnlyOptions(), clock: clock);
            var results = CaptureFinals(detector);
            await Feed(detector, "a", 1000, 1000, 0, 0);
            await detector.DrainAsync();
            var metadata = (IReadOnlyDictionary<string, object>)results.Single().Metadata["vad_performance"];
            Assert.That((double)metadata["stt_after_threshold_time"], Is.EqualTo(0.25));
            Assert.That((double)metadata["silence_threshold_time"], Is.EqualTo(0.2).Within(1e-8));
        }

        private SileroStreamSpeechDetectorEngine Create(ISpeechRecognizer recognizer,
            SileroStreamSpeechDetectorOptions options = null, IEnumerable<ITurnEndGate> gates = null,
            ISpeechDetectorClock clock = null)
        {
            var detector = new SileroStreamSpeechDetectorEngine(new SignalModel(), recognizer,
                options ?? DefaultOptions(), turnEndGates: gates, clock: clock);
            detectors.Add(detector);
            return detector;
        }

        private ControlledRecognizer Controlled(bool honorCancellation = false)
        {
            var recognizer = new ControlledRecognizer(honorCancellation);
            recognizers.Add(recognizer);
            return recognizer;
        }

        private static ConcurrentQueue<SpeechRecognitionUpdate> CaptureRecognitionState(SileroStreamSpeechDetectorEngine detector)
        {
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            // Lifecycle/correlation tests focus on state changes; activity facts have dedicated coverage above.
            detector.RecognitionUpdated += update =>
            {
                if (update.Kind != SpeechRecognitionUpdateKind.Activity) updates.Enqueue(update);
            };
            return updates;
        }

        private static SileroStreamSpeechDetectorOptions DefaultOptions() => new SileroStreamSpeechDetectorOptions
        {
            MinDuration = 0.1, SilenceDurationThreshold = 0.5, SegmentSilenceThreshold = 0.1,
            PrerollBufferCount = 2, RecordingStartedMinDuration = 100
        };

        private static SileroStreamSpeechDetectorOptions FinalOnlyOptions()
        {
            var options = DefaultOptions();
            options.SegmentSilenceThreshold = 10;
            options.SilenceDurationThreshold = 0.2;
            return options;
        }

        private static ConcurrentQueue<SpeechDetectionResult> CaptureFinals(SileroStreamSpeechDetectorEngine detector)
        {
            var results = new ConcurrentQueue<SpeechDetectionResult>();
            detector.SpeechDetected += result => { results.Enqueue(result); return UniTask.CompletedTask; };
            return results;
        }

        private static async UniTask Feed(SileroStreamSpeechDetectorEngine detector, string id, params int[] values)
        {
            foreach (var value in values) await detector.ProcessSamplesAsync(Pcm(value), id);
        }

        private static byte[] Pcm(int value)
        {
            var pcm = new byte[3200]; // 100 ms of mono PCM16 at 16 kHz.
            for (var i = 0; i < pcm.Length; i += 2)
            {
                pcm[i] = (byte)value;
                pcm[i + 1] = (byte)(value >> 8);
            }
            return pcm;
        }

        private static SpeechCompletionSource<bool> NewSignal() =>
            new SpeechCompletionSource<bool>();

        private static async UniTask Within(UniTask task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!task.Status.IsCompleted() && DateTime.UtcNow < deadline) await SpeechAsync.Yield();
            Assert.That(task.Status.IsCompleted(), Is.True, "Asynchronous VAD operation timed out.");
            await task;
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

        private sealed class CapturingGate : TurnEndGateBase
        {
            public ConcurrentQueue<TurnEndRequest> Requests { get; } = new ConcurrentQueue<TurnEndRequest>();
            public override UniTask<TurnEndDecision> ShouldEndTurnAsync(TurnEndRequest request, CancellationToken cancellationToken)
            {
                Requests.Enqueue(request);
                return UniTask.FromResult(new TurnEndDecision { ShouldEnd = true });
            }
        }

        private sealed class FakeClock : ISpeechDetectorClock
        {
            public DateTimeOffset UtcNow => new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
            public double ElapsedSeconds { get; set; }
        }

        private sealed class RecognitionRequest
        {
            public byte[] Audio;
            public CancellationToken Token;
            public readonly SpeechCompletionSource<SpeechRecognitionResult> Completion =
                new SpeechCompletionSource<SpeechRecognitionResult>();
            public void Complete(string text) => Completion.TrySetResult(new SpeechRecognitionResult { Text = text });
        }

        private sealed class ControlledRecognizer : ISpeechRecognizer
        {
            private readonly bool honorCancellation;
            private readonly object sync = new object();
            private readonly List<RecognitionRequest> requests = new List<RecognitionRequest>();
            private SpeechCompletionSource<bool> changed = NewSignal();
            public ControlledRecognizer(bool honorCancellation) { this.honorCancellation = honorCancellation; }
            public int Count { get { lock (sync) return requests.Count; } }
            public UniTask<SpeechRecognitionResult> RecognizeAsync(string sessionId, byte[] audio, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = new RecognitionRequest { Audio = audio, Token = cancellationToken };
                lock (sync)
                {
                    requests.Add(request);
                    changed.TrySetResult(true);
                    changed = NewSignal();
                }
                return honorCancellation ? CompleteWhenCanceledAsync(request) : request.Completion.Task;
            }
            private static async UniTask<SpeechRecognitionResult> CompleteWhenCanceledAsync(RecognitionRequest request)
            {
                using (request.Token.Register(() => request.Completion.TrySetCanceled()))
                    return await request.Completion.Task;
            }
            public async UniTask<RecognitionRequest> RequestAsync(int index)
            {
                while (true)
                {
                    UniTask notification;
                    lock (sync)
                    {
                        if (requests.Count > index) return requests[index];
                        notification = changed.Task;
                    }
                    await Within(notification);
                }
            }
            public void CompleteOutstanding()
            {
                lock (sync) foreach (var request in requests) request.Complete(null);
            }
        }
    }
}

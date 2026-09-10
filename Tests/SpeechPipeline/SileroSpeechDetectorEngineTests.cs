using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.VAD;
using ChatdollKit.SpeechPipeline.VAD.Filters;
using ChatdollKit.SpeechPipeline.VAD.TurnEndGates;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class SileroSpeechDetectorEngineTests
    {
        private sealed class FakeModel : ISileroVadModel
        {
            public readonly Queue<float> Probabilities = new Queue<float>();
            public readonly List<float[]> Inputs = new List<float[]>();
            public readonly List<int> SampleRates = new List<int>();
            public float Probability;
            public bool FailNext;
            public int Resets;
            public int Disposals;
            public UniTask<float> PredictAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Inputs.Add((float[])samples.Clone());
                SampleRates.Add(sampleRate);
                if (FailNext) { FailNext = false; throw new InvalidOperationException("model failed"); }
                return UniTask.FromResult(Probabilities.Count == 0 ? Probability : Probabilities.Dequeue());
            }
            public void ResetStates() { Resets++; }
            public void Dispose() { Disposals++; }
        }

        private sealed class FakeClock : ISpeechDetectorClock
        {
            public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public double ElapsedSeconds { get; set; }
        }

        private sealed class FakeFilter : IAudioFilter
        {
            public Func<byte[], string, byte[]> Transform = (pcm, id) => pcm;
            public Action<string> OnReset;
            public int Resets;
            public byte[] Process(byte[] pcm, string sessionId) => Transform(pcm, sessionId);
            public void ResetSession(string sessionId) { Resets++; OnReset?.Invoke(sessionId); }
        }

        private sealed class TestGate : TurnEndGateBase
        {
            public int Calls;
            public Func<TurnEndRequest, CancellationToken, UniTask<TurnEndDecision>> Evaluate;
            public override UniTask<TurnEndDecision> ShouldEndTurnAsync(TurnEndRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                return Evaluate(request, cancellationToken);
            }
        }

        private static SileroSpeechDetectorOptions Options() => new SileroSpeechDetectorOptions
        {
            MinDuration = 0.125, SilenceDurationThreshold = 0.25
        };
        private static byte[] Pcm(short amplitude, int count = 2000)
        {
            var result = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                result[i * 2] = (byte)amplitude;
                result[i * 2 + 1] = (byte)(amplitude >> 8);
            }
            return result;
        }
        private static byte[] Join(params byte[][] chunks) => chunks.SelectMany(chunk => chunk).ToArray();
        private static void Collect(SileroSpeechDetectorEngine detector, List<SpeechDetectionResult> results)
            => detector.SpeechDetected += result => { results.Add(result); return UniTask.CompletedTask; };
        private static Dictionary<string, object> Timing(SpeechDetectionResult result)
            => (Dictionary<string, object>)result.Metadata["vad_performance"];

        [Test]
        public async NUnitTask DefaultsMatchUpstreamAndOptionsAreCopied()
        {
            var model = new FakeModel { Probability = 0.75f };
            var supplied = new SileroSpeechDetectorOptions();
            var detector = new SileroSpeechDetectorEngine(model, supplied);
            try
            {
                supplied.SpeechProbabilityThreshold = 0.99;
                var options = (SileroSpeechDetectorOptions)detector.GetOptions();
                Assert.That(options.SampleRate, Is.EqualTo(16000));
                Assert.That(options.Channels, Is.EqualTo(1));
                Assert.That(options.VolumeDbThreshold, Is.Null);
                Assert.That(options.SpeechProbabilityThreshold, Is.EqualTo(0.5));
                Assert.That(options.ChunkSize, Is.EqualTo(512));
                Assert.That(options.UseVadIterator, Is.False);
                Assert.That(options.SilenceDurationThreshold, Is.EqualTo(0.5));
                Assert.That(options.MinDuration, Is.EqualTo(0.2));
                Assert.That(options.MaxDuration, Is.EqualTo(10));
                Assert.That(options.PrerollBufferCount, Is.EqualTo(5));
                Assert.That(options.RecordingStartedMinDuration, Is.EqualTo(1.5));
                options.SpeechProbabilityThreshold = 0.99;
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1, 512)), Is.True);
                Assert.That(model.Disposals, Is.Zero);
            }
            finally { await detector.DisposeAsync(); }
            Assert.That(model.Disposals, Is.Zero, "The injected model remains caller-owned.");
        }

        [Test]
        public async NUnitTask DirectProbabilityComparisonIsStrict()
        {
            var model = new FakeModel();
            model.Probabilities.Enqueue(0.5f);
            model.Probabilities.Enqueue(0.5001f);
            var detector = new SileroSpeechDetectorEngine(model, Options());
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1)), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1)), Is.True);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask OutputPreservesDuplicateOnsetAndDurationExcludesPrerollAndTail()
        {
            var model = new FakeModel();
            foreach (float probability in new[] { 0f, 1f, 0f }) model.Probabilities.Enqueue(probability);
            var detector = new SileroSpeechDetectorEngine(model, Options());
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            var preroll = Pcm(1, 1000);
            var onset = Pcm(1000);
            var tail = Pcm(0, 4000);
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(preroll), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(onset), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(tail), Is.False);
                await detector.DrainAsync();
                CollectionAssert.AreEqual(Join(preroll, onset, onset, tail), results.Single().Audio);
                Assert.That(results[0].RecordedDuration, Is.EqualTo(0.125));
                Assert.That(results[0].Text, Is.Null);
                Assert.That(Timing(results[0])["silence_threshold_time"], Is.EqualTo(0.25));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ReusedFilterOutputCannotOverwriteRetainedPreroll()
        {
            var model = new FakeModel();
            foreach (float probability in new[] { 0f, 1f, 0f }) model.Probabilities.Enqueue(probability);
            var reusable = new byte[4000];
            var filter = new FakeFilter
            {
                Transform = (pcm, id) =>
                {
                    Array.Copy(pcm, reusable, pcm.Length);
                    return reusable;
                }
            };
            var options = Options();
            options.SilenceDurationThreshold = 0.125;
            var detector = new SileroSpeechDetectorEngine(model, options, new[] { filter });
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            var preroll = Pcm(1);
            var onset = Pcm(1000);
            var tail = Pcm(0);
            try
            {
                await detector.ProcessSamplesAsync(preroll);
                await detector.ProcessSamplesAsync(onset);
                await detector.ProcessSamplesAsync(tail);
                await detector.DrainAsync();
                CollectionAssert.AreEqual(Join(preroll, onset, onset, tail), results.Single().Audio);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask FilterCleanupRejectsDetectorReentryWithoutBlockingFinalization()
        {
            var filter = new FakeFilter();
            var detector = new SileroSpeechDetectorEngine(new FakeModel(), audioFilters: new[] { filter });
            var errors = new List<Exception>();
            detector.Error += errors.Add;
            filter.OnReset = id => detector.ResetSessionAsync(id).GetAwaiter().GetResult();
            try
            {
                await detector.SetSessionDataAsync("cleanup", "marker", true, createSession: true);
                await detector.FinalizeSessionAsync("cleanup");
                Assert.That(errors.Single(), Is.TypeOf<InvalidOperationException>());
                Assert.That(errors.Single().Message, Does.Contain("Schedule detector control after the callback returns"),
                    "The callback guard must reject reentry before a pending UniTask could throw from GetResult.");
                Assert.That(await detector.GetSessionDataAsync("cleanup", "marker"), Is.Null);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask LargeInputEvaluatesOnlyLatestWindowOnceAndNormalizesPcm16()
        {
            var model = new FakeModel();
            var detector = new SileroSpeechDetectorEngine(model);
            try
            {
                await detector.ProcessSamplesAsync(Join(Pcm(100, 512), Pcm(-32768, 512)));
                Assert.That(model.Inputs.Count, Is.EqualTo(1));
                CollectionAssert.AreEqual(Enumerable.Repeat(-1f, 512), model.Inputs[0]);
                await detector.ProcessSamplesAsync(Pcm(32767, 2048));
                Assert.That(model.Inputs.Count, Is.EqualTo(2));
                CollectionAssert.AreEqual(Enumerable.Repeat(32767 / 32768f, 512), model.Inputs[1]);
                CollectionAssert.AreEqual(new[] { 16000, 16000 }, model.SampleRates);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask SmallInputsAccumulateThenEvaluateOverlappingLatestWindows()
        {
            var model = new FakeModel();
            var detector = new SileroSpeechDetectorEngine(model);
            try
            {
                await detector.ProcessSamplesAsync(Pcm(100, 128));
                await detector.ProcessSamplesAsync(Pcm(200, 128));
                Assert.That(model.Inputs, Is.Empty);
                await detector.ProcessSamplesAsync(Pcm(300, 256));
                await detector.ProcessSamplesAsync(Pcm(400, 128));
                Assert.That(model.Inputs.Count, Is.EqualTo(2));
                CollectionAssert.AreEqual(Enumerable.Repeat(100 / 32768f, 128).Concat(Enumerable.Repeat(200 / 32768f, 128)).Concat(Enumerable.Repeat(300 / 32768f, 256)), model.Inputs[0]);
                CollectionAssert.AreEqual(Enumerable.Repeat(200 / 32768f, 128).Concat(Enumerable.Repeat(300 / 32768f, 256)).Concat(Enumerable.Repeat(400 / 32768f, 128)), model.Inputs[1]);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask OrdinaryResetKeepsVadBufferAndStrongResetClearsItWithoutDeletingData()
        {
            foreach (bool strong in new[] { false, true })
            {
                var model = new FakeModel();
                var detector = new SileroSpeechDetectorEngine(model);
                try
                {
                    await detector.SetSessionDataAsync("reset", "metadata", "kept", createSession: true);
                    await detector.ProcessSamplesAsync(Pcm(100, 256), "reset");
                    if (strong) await detector.ResetSpeechInputAsync("reset", clearPreroll: false);
                    else await detector.ResetSessionAsync("reset");
                    await detector.ProcessSamplesAsync(Pcm(200, 256), "reset");
                    Assert.That(model.Inputs.Count, Is.EqualTo(strong ? 0 : 1));
                    if (!strong)
                        CollectionAssert.AreEqual(Enumerable.Repeat(100 / 32768f, 256).Concat(Enumerable.Repeat(200 / 32768f, 256)), model.Inputs[0]);
                    Assert.That(await detector.GetSessionDataAsync("reset", "metadata"), Is.EqualTo("kept"));
                }
                finally { await detector.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask MaximumTakesPriorityOverSilenceAndMinimumDuration()
        {
            var model = new FakeModel();
            model.Probabilities.Enqueue(1);
            model.Probabilities.Enqueue(0);
            var options = Options();
            options.MaxDuration = 0.25;
            options.MinDuration = 0.5;
            options.SilenceDurationThreshold = 0.125;
            var detector = new SileroSpeechDetectorEngine(model, options);
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0)), Is.False);
                await detector.DrainAsync();
                Assert.That(results.Single().RecordedDuration, Is.EqualTo(0.25));
                Assert.That(results[0].Metadata, Is.Null, "Maximum is checked before a first silence-threshold timestamp is recorded.");
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask SessionVolumeOverrideIsClearedByOnsetResetAsInUpstream()
        {
            var model = new FakeModel { Probability = 1 };
            var options = Options();
            options.VolumeDbThreshold = 0;
            options.SilenceDurationThreshold = 0.125;
            var detector = new SileroSpeechDetectorEngine(model, options);
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                await detector.SetVolumeDbThresholdAsync("override", -60);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000), "default-threshold"), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000), "override"), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000), "override"), Is.False);
                await detector.DrainAsync();
                Assert.That(results.Single().RecordedDuration, Is.EqualTo(0.125));
                Assert.That(results[0].SessionId, Is.EqualTo("override"));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ModelFailureIsReportedAndTreatedAsUnvoiced()
        {
            var model = new FakeModel { FailNext = true, Probability = 1 };
            var detector = new SileroSpeechDetectorEngine(model, Options());
            var errors = new List<Exception>();
            detector.Error += errors.Add;
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.False);
                Assert.That(errors.Single(), Is.TypeOf<InvalidOperationException>());
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.True);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask GateHoldUsesAudioSilenceWhilePerformanceUsesInjectedClock()
        {
            var model = new FakeModel();
            model.Probabilities.Enqueue(1);
            var clock = new FakeClock { ElapsedSeconds = 10 };
            var thresholdTime = clock.UtcNow;
            TurnEndRequest gateRequest = null;
            var gate = new TestGate
            {
                Evaluate = (request, token) =>
                {
                    gateRequest = request;
                    return UniTask.FromResult(new TurnEndDecision { Timeout = 0.25, Reason = "hold" });
                }
            };
            var detector = new SileroSpeechDetectorEngine(model, Options(), turnEndGates: new[] { gate }, clock: clock);
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                await detector.ProcessSamplesAsync(Pcm(1000));
                await detector.ProcessSamplesAsync(Pcm(0));
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0)), Is.True);
                Assert.That(gateRequest.RecordedDuration, Is.EqualTo(0.125));
                Assert.That(gateRequest.SilenceDuration, Is.EqualTo(0.25));
                Assert.That(gateRequest.SampleRate, Is.EqualTo(16000));
                Assert.That(gateRequest.Channels, Is.EqualTo(1));
                Assert.That(gateRequest.Text, Is.Null);
                clock.ElapsedSeconds = 10.75;
                clock.UtcNow = clock.UtcNow.AddSeconds(0.75);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0)), Is.True, "Elapsed wall time alone does not exhaust the audio timeout.");
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0)), Is.False);
                await detector.DrainAsync();
                Assert.That(gate.Calls, Is.EqualTo(1));
                var timing = Timing(results.Single());
                Assert.That(timing["speech_end_at"], Is.EqualTo(thresholdTime.AddSeconds(-0.25)));
                Assert.That(timing["silence_threshold_time"], Is.EqualTo(0.25));
                Assert.That(timing["stt_after_threshold_time"], Is.Null);
                Assert.That(timing["turn_end_gate_time"], Is.EqualTo(0.75));
                Assert.That(timing["turn_end_gate_held"], Is.EqualTo(true));
                Assert.That(results[0].RecordedDuration, Is.EqualTo(0.125));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask MaximumOverridesInfiniteGateHoldAndFinalizesPerformance()
        {
            var model = new FakeModel();
            model.Probabilities.Enqueue(1);
            var clock = new FakeClock { ElapsedSeconds = 5 };
            var options = Options();
            options.MaxDuration = 0.5;
            var gate = new TestGate { Evaluate = (request, token) => UniTask.FromResult(new TurnEndDecision { Reason = "infinite" }) };
            var detector = new SileroSpeechDetectorEngine(model, options, turnEndGates: new[] { gate }, clock: clock);
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                await detector.ProcessSamplesAsync(Pcm(1000));
                await detector.ProcessSamplesAsync(Pcm(0));
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0)), Is.True);
                clock.ElapsedSeconds = 8;
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0)), Is.False);
                await detector.DrainAsync();
                Assert.That(results.Single().RecordedDuration, Is.EqualTo(0.5));
                Assert.That(Timing(results[0])["turn_end_gate_time"], Is.EqualTo(3));
                Assert.That(Timing(results[0])["turn_end_gate_held"], Is.EqualTo(true));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ResumedSpeechClearsPriorGateCandidateTiming()
        {
            var model = new FakeModel();
            foreach (float probability in new[] { 1f, 0f, 0f, 1f, 1f }) model.Probabilities.Enqueue(probability);
            var options = Options();
            options.MaxDuration = 0.75;
            var gate = new TestGate { Evaluate = (request, token) => UniTask.FromResult(new TurnEndDecision()) };
            var detector = new SileroSpeechDetectorEngine(model, options, turnEndGates: new[] { gate });
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                await detector.ProcessSamplesAsync(Pcm(1000));
                await detector.ProcessSamplesAsync(Pcm(0));
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0)), Is.True);
                await detector.ProcessSamplesAsync(Pcm(1000));
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 4000)), Is.False);
                await detector.DrainAsync();
                Assert.That(gate.Calls, Is.EqualTo(1));
                Assert.That(results.Single().Metadata, Is.Null);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ConversionAndFiltersRunInOrderAndInferenceSeesFilteredAudio()
        {
            var order = new List<string>();
            var first = new FakeFilter
            {
                Transform = (pcm, id) => { order.Add("first"); CollectionAssert.AreEqual(Pcm(1000), pcm); return Pcm(2000); }
            };
            var second = new FakeFilter { Transform = (pcm, id) => { order.Add("second"); return pcm; } };
            var model = new FakeModel();
            var detector = new SileroSpeechDetectorEngine(model, Options(), audioFilters: new[] { first, second });
            detector.ToLinear16 = bytes => { order.Add("convert"); return Pcm(1000); };
            try
            {
                await detector.ProcessSamplesAsync(new byte[] { 99 });
                CollectionAssert.AreEqual(new[] { "convert", "first", "second" }, order);
                CollectionAssert.AreEqual(Enumerable.Repeat(2000 / 32768f, 512), model.Inputs.Single());
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask EmptyFilterOutputKeepsRecordingAndSkipsMuteUntilAudioArrives()
        {
            var model = new FakeModel { Probability = 1 };
            var filter = new FakeFilter();
            var detector = new SileroSpeechDetectorEngine(model, Options(), audioFilters: new[] { filter });
            int muteChecks = 0;
            bool muted = false;
            detector.ShouldMute = () => { muteChecks++; return muted; };
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.True);
                muted = true;
                filter.Transform = (pcm, id) => new byte[0];
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.True);
                Assert.That(muteChecks, Is.EqualTo(1));
                filter.Transform = (pcm, id) => pcm;
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.False);
                Assert.That(muteChecks, Is.EqualTo(2));
                Assert.That(model.Inputs.Count, Is.EqualTo(1));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask FinalizeResetsFilterStateAndDoesNotDisposeInjectedModelOrFlushAudio()
        {
            var model = new FakeModel { Probability = 1 };
            var filter = new FakeFilter();
            var detector = new SileroSpeechDetectorEngine(model, Options(), audioFilters: new[] { filter });
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                await detector.ProcessSamplesAsync(Pcm(1000));
                await detector.FinalizeSessionAsync();
                await detector.FinalizeSessionAsync();
                await detector.DrainAsync();
                Assert.That(filter.Resets, Is.EqualTo(2));
                Assert.That(results, Is.Empty);
            }
            finally { await detector.DisposeAsync(); }
            Assert.That(model.Disposals, Is.Zero);
        }

        [Test]
        public async NUnitTask ProbabilityThresholdUpdateResetsExistingIteratorAndChangesDetection()
        {
            var model = new FakeModel { Probability = 0.5f };
            var detector = new SileroSpeechDetectorEngine(model);
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.False);
                int resets = model.Resets;
                await detector.SetSpeechProbabilityThresholdAsync(0.4);
                Assert.That(model.Resets, Is.EqualTo(resets + 1));
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.True);
                Assert.That(((SileroSpeechDetectorOptions)detector.GetOptions()).SpeechProbabilityThreshold, Is.EqualTo(0.4));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask TypedOptionsUpdateResetsProbabilityStateAndPreservesSessionAmplitudeThresholds()
        {
            var model = new FakeModel { Probability = 0.75f };
            var options = Options();
            options.VolumeDbThreshold = -40;
            var detector = new SileroSpeechDetectorEngine(model, options);
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100), "existing"), Is.False);
                await detector.SetVolumeDbThresholdAsync("override", -6);
                int resets = model.Resets;
                var next = (SileroSpeechDetectorOptions)detector.GetOptions();
                next.VolumeDbThreshold = -60;
                next.SpeechProbabilityThreshold = 0.6;
                await detector.UpdateOptionsAsync(next);
                next.SpeechProbabilityThreshold = 0.99;
                next.VolumeDbThreshold = 0;
                var snapshot = detector.GetOptions();
                Assert.That(snapshot, Is.TypeOf<SileroSpeechDetectorOptions>());
                Assert.That(((SileroSpeechDetectorOptions)snapshot).SpeechProbabilityThreshold, Is.EqualTo(0.6));
                Assert.That(((SileroSpeechDetectorOptions)snapshot).VolumeDbThreshold, Is.EqualTo(-60));
                Assert.That(model.Resets, Is.EqualTo(resets + 2));
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100), "existing"), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000), "override"), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100), "new"), Is.True);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RuntimeVolumeUpdateCanDisableAndReenableTheCheckForExistingSessions()
        {
            var model = new FakeModel { Probability = 0.75f };
            var options = Options();
            options.VolumeDbThreshold = -40;
            var detector = new SileroSpeechDetectorEngine(model, options);
            var voiced = new List<string>();
            detector.Voiced += id => { voiced.Add(id); return UniTask.CompletedTask; };
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 512), "existing"), Is.False);
                await detector.SetVolumeDbThresholdAsync("override", -6);
                var next = (SileroSpeechDetectorOptions)detector.GetOptions();
                next.VolumeDbThreshold = null;
                await detector.UpdateRuntimeOptionsAsync(next);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1, 512), "existing"), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1, 512), "override"), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1, 512), "new"), Is.True);
                CollectionAssert.AreEqual(new[] { "existing", "override", "new" }, voiced);
                voiced.Clear();

                next.VolumeDbThreshold = -40;
                await detector.UpdateRuntimeOptionsAsync(next);
                await detector.ProcessSamplesAsync(Pcm(100, 512), "existing");
                await detector.ProcessSamplesAsync(Pcm(100, 512), "override");
                await detector.ProcessSamplesAsync(Pcm(100, 512), "new");
                Assert.That(voiced, Is.Empty);
                next.VolumeDbThreshold = -60;
                await detector.UpdateRuntimeOptionsAsync(next);
                await detector.ProcessSamplesAsync(Pcm(100, 512), "existing");
                CollectionAssert.AreEqual(new[] { "existing" }, voiced);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RuntimeProbabilityUpdateClearsRememberedSpeechAfterIteratorReset()
        {
            var options = Options();
            options.UseVadIterator = true;
            var model = new FakeModel { Probability = 0.75f };
            var detector = new SileroSpeechDetectorEngine(model, options);
            int voiced = 0;
            detector.Voiced += id => { voiced++; return UniTask.CompletedTask; };
            try
            {
                await detector.ProcessSamplesAsync(Pcm(1000, 512));
                await detector.ProcessSamplesAsync(Pcm(1000, 512));
                Assert.That(voiced, Is.EqualTo(2));
                var next = (SileroSpeechDetectorOptions)detector.GetOptions();
                next.SpeechProbabilityThreshold = 0.9;
                await detector.UpdateRuntimeOptionsAsync(next);
                await detector.ProcessSamplesAsync(Pcm(1000, 512));
                Assert.That(voiced, Is.EqualTo(2), "A reset iterator has no boundary when probability stays below its new threshold.");
                model.Probability = 0.95f;
                await detector.ProcessSamplesAsync(Pcm(1000, 512));
                Assert.That(voiced, Is.EqualTo(3));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RuntimeIteratorChangeRejectsWholeUpdateBeforeChangingModelOrThresholds()
        {
            var model = new FakeModel { Probability = 0.5f };
            var detector = new SileroSpeechDetectorEngine(model, Options());
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.False);
                int resets = model.Resets;
                var next = (SileroSpeechDetectorOptions)detector.GetOptions();
                next.UseVadIterator = true;
                next.SpeechProbabilityThreshold = 0.1;
                next.SilenceDurationThreshold = 1;
                var error = await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await detector.UpdateRuntimeOptionsAsync(next));
                Assert.That(error.Message, Does.Contain("Restart required").And.Contain("UseVadIterator"));
                Assert.That(model.Resets, Is.EqualTo(resets));
                Assert.That(detector.GetOptions().SilenceDurationThreshold, Is.EqualTo(0.25));
                Assert.That(((SileroSpeechDetectorOptions)detector.GetOptions()).UseVadIterator, Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000)), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000), "new"), Is.False);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask OptionsUpdateRejectsFormatAndWrongTypeWithoutChangingSettings()
        {
            var detector = new SileroSpeechDetectorEngine(new FakeModel(), Options());
            try
            {
                var wrongFormat = (SileroSpeechDetectorOptions)detector.GetOptions();
                wrongFormat.SampleRate = 8000;
                wrongFormat.ChunkSize = 256;
                wrongFormat.SpeechProbabilityThreshold = 0.1;
                await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await detector.UpdateOptionsAsync(wrongFormat));
                await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await detector.UpdateOptionsAsync(new StandardSpeechDetectorOptions()));
                await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await detector.ProcessSamplesAsync(new byte[] { 1 }));
                Assert.That(detector.SampleRate, Is.EqualTo(16000));
                Assert.That(((SileroSpeechDetectorOptions)detector.GetOptions()).ChunkSize, Is.EqualTo(512));
                Assert.That(((SileroSpeechDetectorOptions)detector.GetOptions()).SpeechProbabilityThreshold, Is.EqualTo(0.5));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask IteratorModeUsesInclusiveThresholdAndHoldsThroughBriefLowProbabilities()
        {
            var model = new FakeModel();
            foreach (float probability in new[] { 0.5f, 0.8f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f }) model.Probabilities.Enqueue(probability);
            var options = Options();
            options.UseVadIterator = true;
            int voiced = 0;
            var detector = new SileroSpeechDetectorEngine(model, options);
            detector.Voiced += id => { voiced++; return UniTask.CompletedTask; };
            try
            {
                for (int frame = 0; frame < 7; frame++)
                    Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 512)), Is.True);
                Assert.That(voiced, Is.EqualTo(6), "Four low-probability frames remain voiced before the iterator's 100 ms silence condition is exceeded.");
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DisposeCancelsAnActiveInlineGateBeforeWaitingForInputToFinish()
        {
            var model = new FakeModel();
            model.Probabilities.Enqueue(1);
            var options = Options();
            options.SilenceDurationThreshold = 0.125;
            var entered = new SpeechCompletionSource<bool>();
            var release = new SpeechCompletionSource<TurnEndDecision>();
            CancellationToken gateToken = default;
            var gate = new TestGate
            {
                Evaluate = (request, token) =>
                {
                    gateToken = token;
                    token.Register(() => release.TrySetCanceled());
                    entered.TrySetResult(true);
                    return release.Task;
                }
            };
            var detector = new SileroSpeechDetectorEngine(model, options, turnEndGates: new[] { gate });
            UniTask<bool>? processing = null;
            UniTask? closing = null;
            try
            {
                await detector.ProcessSamplesAsync(Pcm(1000));
                processing = detector.ProcessSamplesAsync(Pcm(0));
                await entered.Task;
                closing = detector.DisposeAsync();
                Assert.That(gateToken.IsCancellationRequested, Is.True, "Dispose must cancel active gate work before it waits for the serialized operation.");
            }
            finally
            {
                release.TrySetResult(new TurnEndDecision { ShouldEnd = true });
                if (processing != null)
                    try { await processing.Value; } catch (OperationCanceledException) { }
                if (closing != null) await closing.Value;
                else await detector.DisposeAsync();
            }
        }
    }
}

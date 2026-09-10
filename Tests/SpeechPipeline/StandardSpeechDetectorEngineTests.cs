using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.VAD;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class StandardSpeechDetectorEngineTests
    {
        private static StandardSpeechDetectorOptions Options() => new StandardSpeechDetectorOptions
        {
            SampleRate = 10, MinDuration = 0.1, SilenceDurationThreshold = 0.5
        };

        private static byte[] Pcm(short amplitude, int count)
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
        private static SpeechCompletionSource<bool> Signal() => new SpeechCompletionSource<bool>();
        private static void Collect(StandardSpeechDetectorEngine detector, List<SpeechDetectionResult> results)
            => detector.SpeechDetected += result => { results.Add(result); return UniTask.CompletedTask; };

        [Test]
        public async NUnitTask DefaultsMatchUpstreamAndOptionsAreCopied()
        {
            var supplied = new StandardSpeechDetectorOptions();
            var detector = new StandardSpeechDetectorEngine(supplied);
            try
            {
                supplied.VolumeDbThreshold = 0;
                var options = (StandardSpeechDetectorOptions)detector.GetOptions();
                Assert.That(options.SampleRate, Is.EqualTo(16000));
                Assert.That(options.Channels, Is.EqualTo(1));
                Assert.That(options.VolumeDbThreshold, Is.EqualTo(-40));
                Assert.That(options.SilenceDurationThreshold, Is.EqualTo(0.5));
                Assert.That(options.MinDuration, Is.EqualTo(0.2));
                Assert.That(options.MaxDuration, Is.EqualTo(10));
                Assert.That(options.PrerollBufferCount, Is.EqualTo(5));
                Assert.That(options.RecordingStartedMinDuration, Is.EqualTo(1.5));
                Assert.That(options.RecordingStartedMinTextLength, Is.EqualTo(2));
                options.VolumeDbThreshold = 0;
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 512)), Is.True);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask OutputPreservesDuplicateOnsetPrerollAndTrailingSilence()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            var preroll = Pcm(0, 2);
            var onset = Pcm(1000, 3);
            var tail = Pcm(0, 5);
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(preroll, "pcm"), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(onset, "pcm"), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(tail, "pcm"), Is.False);
                await detector.DrainAsync();
                Assert.That(results.Count, Is.EqualTo(1));
                CollectionAssert.AreEqual(Join(preroll, onset, onset, tail), results[0].Audio);
                Assert.That(results[0].RecordedDuration, Is.EqualTo(0.3).Within(1e-12));
                Assert.That(results[0].SessionId, Is.EqualTo("pcm"));
                Assert.That(results[0].Text, Is.Null);
                Assert.That(results[0].Metadata, Is.Null);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask SilenceAndMinimumDurationTakePriorityOverMaximum()
        {
            var options = Options();
            options.MaxDuration = 1;
            options.MinDuration = 0.4;
            var detector = new StandardSpeechDetectorEngine(options);
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 3)), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0, 7)), Is.False);
                await detector.DrainAsync();
                Assert.That(results, Is.Empty, "The 0.3 s utterance is dropped even though total recording reached 1 s.");
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask MaximumIncludesInternalSilenceAndDoesNotApplyMinimum()
        {
            var options = Options();
            options.MaxDuration = 1;
            options.MinDuration = 2;
            var detector = new StandardSpeechDetectorEngine(options);
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                await detector.ProcessSamplesAsync(Pcm(1000, 5));
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0, 2)), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 3)), Is.False);
                await detector.DrainAsync();
                Assert.That(results.Single().RecordedDuration, Is.EqualTo(1).Within(1e-12));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask FirstChunkDoesNotCheckMaximumOrRecordingStarted()
        {
            var options = Options();
            options.MaxDuration = 0.2;
            options.RecordingStartedMinDuration = 0.2;
            var detector = new StandardSpeechDetectorEngine(options);
            var results = new List<SpeechDetectionResult>();
            int started = 0;
            detector.RecordingStarted += id => { started++; return UniTask.CompletedTask; };
            Collect(detector, results);
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 3)), Is.True);
                await detector.DrainAsync();
                Assert.That(started, Is.Zero);
                Assert.That(results, Is.Empty);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 1)), Is.False);
                await detector.DrainAsync();
                Assert.That(started, Is.EqualTo(1));
                Assert.That(results.Single().RecordedDuration, Is.EqualTo(0.4).Within(1e-12));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask SessionThresholdOverridesAreIndependentAndSurviveReset()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            try
            {
                await detector.SetVolumeDbThresholdAsync("quiet", -6);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 3), "quiet"), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 3), "normal"), Is.True);
                await detector.ResetSessionAsync("quiet");
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 3), "quiet"), Is.False);
                await detector.SetVolumeDbThresholdAsync("quiet", -60);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 3), "quiet"), Is.True);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask TypedOptionsUpdateCopiesValuesAndPreservesExistingSessionThresholds()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 2), "existing"), Is.False);
                await detector.SetVolumeDbThresholdAsync("override", -6);
                var next = (StandardSpeechDetectorOptions)detector.GetOptions();
                next.VolumeDbThreshold = -60;
                next.MinDuration = 0.4;
                await detector.UpdateOptionsAsync(next);
                next.VolumeDbThreshold = 0;
                var snapshot = detector.GetOptions();
                Assert.That(snapshot, Is.TypeOf<StandardSpeechDetectorOptions>());
                Assert.That(((StandardSpeechDetectorOptions)snapshot).VolumeDbThreshold, Is.EqualTo(-60));
                Assert.That(snapshot.MinDuration, Is.EqualTo(0.4));
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 2), "existing"), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 2), "override"), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 2), "new"), Is.True);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RuntimeVolumeUpdateAppliesToExistingSessionsAndReplacesOverrides()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 2), "existing"), Is.False);
                await detector.SetVolumeDbThresholdAsync("override", -6);
                var next = (StandardSpeechDetectorOptions)detector.GetOptions();
                next.VolumeDbThreshold = -60;
                await detector.UpdateRuntimeOptionsAsync(next);
                next.VolumeDbThreshold = 0;
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 2), "existing"), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 2), "override"), Is.True);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 2), "new"), Is.True);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RuntimeSilenceUpdateEndsCurrentRecordingAndPreservesUnchangedVolumeOverrides()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                await detector.SetVolumeDbThresholdAsync("override", -6);
                await detector.ProcessSamplesAsync(Pcm(1000, 2), "recording");
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0, 2), "recording"), Is.True);
                var next = detector.GetOptions();
                next.SilenceDurationThreshold = 0.3;
                await detector.UpdateRuntimeOptionsAsync(next);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 2), "override"), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(0, 1), "recording"), Is.False);
                await detector.DrainAsync();
                Assert.That(results.Single().RecordedDuration, Is.EqualTo(0.2).Within(1e-12));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RuntimePrerollChangeRejectsWholeUpdateBeforeChangingCurrentSessions()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            try
            {
                await detector.ProcessSamplesAsync(Pcm(100, 2));
                var next = (StandardSpeechDetectorOptions)detector.GetOptions();
                next.PrerollBufferCount++;
                next.VolumeDbThreshold = -60;
                next.SilenceDurationThreshold = 0.1;
                var error = await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await detector.UpdateRuntimeOptionsAsync(next));
                Assert.That(error.Message, Does.Contain("Restart required").And.Contain("PrerollBufferCount"));
                Assert.That(detector.GetOptions().PrerollBufferCount, Is.EqualTo(5));
                Assert.That(detector.GetOptions().SilenceDurationThreshold, Is.EqualTo(0.5));
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 2)), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(100, 2), "new"), Is.False);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask OptionsUpdateRejectsAudioFormatAndWrongTypeWithoutChangingSettings()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            try
            {
                var wrongRate = (StandardSpeechDetectorOptions)detector.GetOptions();
                wrongRate.SampleRate = 20;
                wrongRate.VolumeDbThreshold = -60;
                await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await detector.UpdateOptionsAsync(wrongRate));
                var wrongChannels = (StandardSpeechDetectorOptions)detector.GetOptions();
                wrongChannels.Channels = 2;
                await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await detector.UpdateOptionsAsync(wrongChannels));
                await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await detector.UpdateOptionsAsync(new SpeechDetectorOptions { SampleRate = 10 }));
                Assert.That(detector.SampleRate, Is.EqualTo(10));
                Assert.That(detector.Channels, Is.EqualTo(1));
                Assert.That(((StandardSpeechDetectorOptions)detector.GetOptions()).VolumeDbThreshold, Is.EqualTo(-40));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask AmplitudeComparisonIsStrictAndHandlesMinimumPcm16Value()
        {
            var options = Options();
            options.VolumeDbThreshold = 0;
            var detector = new StandardSpeechDetectorEngine(options);
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(32767, 3)), Is.False);
                Assert.That(await detector.ProcessSamplesAsync(Pcm(-32768, 3)), Is.True);
                await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await detector.ProcessSamplesAsync(new byte[] { 1 }));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask OrdinaryAndOptionalPrerollResetPreserveAudioWhileStrongResetClearsIt()
        {
            for (int resetMode = 0; resetMode < 3; resetMode++)
            {
                var detector = new StandardSpeechDetectorEngine(Options());
                var results = new List<SpeechDetectionResult>();
                Collect(detector, results);
                var preroll = Pcm(10, 2);
                var onset = Pcm(1000, 3);
                var tail = Pcm(0, 5);
                try
                {
                    await detector.ProcessSamplesAsync(preroll);
                    if (resetMode == 0) await detector.ResetSessionAsync();
                    else await detector.ResetSpeechInputAsync(clearPreroll: resetMode == 2);
                    await detector.ProcessSamplesAsync(onset);
                    await detector.ProcessSamplesAsync(tail);
                    await detector.DrainAsync();
                    CollectionAssert.AreEqual(resetMode == 2 ? Join(onset, onset, tail) : Join(preroll, onset, onset, tail), results.Single().Audio);
                }
                finally { await detector.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask SessionDataRequiresCreationSurvivesResetsAndIsDeletedWithoutFlush()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                await detector.SetSessionDataAsync("data", "key", 1);
                Assert.That(await detector.GetSessionDataAsync("data", "key"), Is.Null);
                await detector.SetSessionDataAsync("data", "key", 2, createSession: true);
                await detector.ResetSessionAsync("data");
                await detector.ResetSpeechInputAsync("data");
                Assert.That(await detector.GetSessionDataAsync("data", "key"), Is.EqualTo(2));
                await detector.ProcessSamplesAsync(Pcm(1000, 3), "data");
                await detector.FinalizeSessionAsync("data");
                await detector.FinalizeSessionAsync("data");
                await detector.DrainAsync();
                Assert.That(await detector.IsRecordingAsync("data"), Is.False);
                Assert.That(await detector.GetSessionDataAsync("data", "key"), Is.Null);
                Assert.That(results, Is.Empty);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RecordingStartedFiresOncePerTurnAndCustomConditionCanSuppressIt()
        {
            var options = Options();
            options.RecordingStartedMinDuration = 0.2;
            var detector = new StandardSpeechDetectorEngine(options);
            int starts = 0;
            detector.RecordingStarted += id => { starts++; return UniTask.CompletedTask; };
            try
            {
                for (int turn = 0; turn < 2; turn++)
                {
                    await detector.ProcessSamplesAsync(Pcm(1000, 2));
                    await detector.ProcessSamplesAsync(Pcm(1000, 1));
                    await detector.ProcessSamplesAsync(Pcm(1000, 1));
                    await detector.ProcessSamplesAsync(Pcm(0, 5));
                    await detector.DrainAsync();
                    Assert.That(starts, Is.EqualTo(turn + 1));
                }
                detector.ShouldTriggerRecordingStarted = (text, session) => false;
                await detector.ProcessSamplesAsync(Pcm(1000, 3));
                await detector.ProcessSamplesAsync(Pcm(1000, 3));
                await detector.DrainAsync();
                Assert.That(starts, Is.EqualTo(2));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask CallbackFailuresDoNotSkipOtherHandlersOrFaultProcessing()
        {
            var options = Options();
            options.RecordingStartedMinDuration = 0.2;
            var detector = new StandardSpeechDetectorEngine(options);
            int voiced = 0, started = 0, detected = 0, errors = 0;
            detector.Error += error => throw new InvalidOperationException("error observer");
            detector.Error += error => Interlocked.Increment(ref errors);
            detector.Voiced += id => throw new InvalidOperationException("voiced");
            detector.Voiced += id => { voiced++; return UniTask.CompletedTask; };
            detector.RecordingStarted += id => throw new InvalidOperationException("started");
            detector.RecordingStarted += id => { Interlocked.Increment(ref started); return UniTask.CompletedTask; };
            detector.SpeechDetected += result => throw new InvalidOperationException("detected");
            detector.SpeechDetected += result => { Interlocked.Increment(ref detected); return UniTask.CompletedTask; };
            try
            {
                await detector.ProcessSamplesAsync(Pcm(1000, 3));
                await detector.ProcessSamplesAsync(Pcm(1000, 2));
                await detector.ProcessSamplesAsync(Pcm(0, 5));
                await detector.DrainAsync();
                Assert.That(voiced, Is.EqualTo(2));
                Assert.That(started, Is.EqualTo(1));
                Assert.That(detected, Is.EqualTo(1));
                Assert.That(errors, Is.EqualTo(4));
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask InputAndControlAreSerializedAndInputIsCopiedBeforeWaiting()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            var entered = Signal();
            var release = Signal();
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            detector.Voiced += async id => { entered.TrySetResult(true); await release.Task; };
            var onset = Pcm(1000, 3);
            var savedOnset = (byte[])onset.Clone();
            var first = detector.ProcessSamplesAsync(onset);
            UniTask<bool>? second = null;
            UniTask? reset = null;
            try
            {
                await entered.Task;
                var tail = Pcm(0, 5);
                second = detector.ProcessSamplesAsync(tail);
                reset = detector.ResetSessionAsync();
                Assert.That(second.Value.Status.IsCompleted(), Is.False);
                Assert.That(reset.Value.Status.IsCompleted(), Is.False);
                Array.Clear(onset, 0, onset.Length);
                release.TrySetResult(true);
                Assert.That(await first, Is.True);
                Assert.That(await second.Value, Is.False);
                await reset.Value;
                await detector.DrainAsync();
                CollectionAssert.AreEqual(Join(savedOnset, savedOnset, tail), results.Single().Audio);
            }
            finally
            {
                release.TrySetResult(true);
                await first;
                if (second != null) await second.Value;
                if (reset != null) await reset.Value;
                await detector.DisposeAsync();
            }
        }

        [Test]
        public async NUnitTask AwaitingDetectorControlInsideCallbackReportsAnErrorInsteadOfDeadlocking()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            var errors = new List<Exception>();
            detector.Error += errors.Add;
            detector.Voiced += id => detector.ResetSessionAsync(id);
            try
            {
                Assert.That(await detector.ProcessSamplesAsync(Pcm(1000, 3)), Is.True);
                Assert.That(errors.Single(), Is.TypeOf<InvalidOperationException>());
                await detector.ResetSessionAsync();
                Assert.That(await detector.IsRecordingAsync(), Is.False);
            }
            finally { await detector.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask CancellingQueuedInputDoesNotApplyItsAudio()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            var entered = Signal();
            var release = Signal();
            detector.Voiced += async id => { entered.TrySetResult(true); await release.Task; };
            var first = detector.ProcessSamplesAsync(Pcm(1000, 3));
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    await entered.Task;
                    var queued = detector.ProcessSamplesAsync(Pcm(0, 5), cancellationToken: cancellation.Token);
                    cancellation.Cancel();
                    try { await queued; Assert.Fail("Queued input must be cancelled."); }
                    catch (OperationCanceledException) { }
                    release.TrySetResult(true);
                    Assert.That(await first, Is.True);
                    Assert.That(await detector.IsRecordingAsync(), Is.True);
                }
                finally { release.TrySetResult(true); await first; await detector.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask MuteDiscardsRecordingAndPreroll()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            var results = new List<SpeechDetectionResult>();
            Collect(detector, results);
            try
            {
                await detector.ProcessSamplesAsync(Pcm(2000, 3));
                detector.ShouldMute = () => true;
                Assert.That(await detector.ProcessSamplesAsync(Pcm(3000, 3)), Is.False);
                detector.ShouldMute = () => false;
                var onset = Pcm(1000, 3);
                var tail = Pcm(0, 5);
                await detector.ProcessSamplesAsync(onset);
                await detector.ProcessSamplesAsync(tail);
                await detector.DrainAsync();
                CollectionAssert.AreEqual(Join(onset, onset, tail), results.Single().Audio);
            }
            finally { await detector.DisposeAsync(); }
        }

        private sealed class TestAudioStream : IAsyncEnumerable<byte[]>, IAsyncEnumerator<byte[]>
        {
            private readonly byte[][] chunks;
            private readonly bool failAtEnd;
            private int index = -1;
            public bool Disposed;
            public TestAudioStream(bool failAtEnd, params byte[][] chunks) { this.failAtEnd = failAtEnd; this.chunks = chunks; }
            public byte[] Current => chunks[index];
            public IAsyncEnumerator<byte[]> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
            public ValueTask<bool> MoveNextAsync()
            {
                index++;
                if (index == chunks.Length && failAtEnd) throw new InvalidOperationException("input failed");
                return new ValueTask<bool>(index < chunks.Length);
            }
            public ValueTask DisposeAsync() { Disposed = true; return default; }
        }

        [Test]
        public async NUnitTask StreamEndAndInputFailureDisposeEnumeratorAndDiscardUnfinishedAudio()
        {
            foreach (bool fail in new[] { false, true })
            {
                var detector = new StandardSpeechDetectorEngine(Options());
                var results = new List<SpeechDetectionResult>();
                Collect(detector, results);
                var stream = new TestAudioStream(fail, Pcm(1000, 3));
                try
                {
                    try
                    {
                        await detector.ProcessStreamAsync(stream);
                        Assert.That(fail, Is.False);
                    }
                    catch (InvalidOperationException) { Assert.That(fail, Is.True); }
                    await detector.DrainAsync();
                    Assert.That(stream.Disposed, Is.True);
                    Assert.That(await detector.IsRecordingAsync(), Is.False);
                    Assert.That(results, Is.Empty);
                }
                finally { await detector.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask DisposeIsIdempotentAndRejectsNewInput()
        {
            var detector = new StandardSpeechDetectorEngine(Options());
            await detector.ProcessSamplesAsync(Pcm(1000, 3));
            var first = detector.DisposeAsync();
            Assert.That(detector.DisposeAsync(), Is.EqualTo(first));
            await first;
            try { await detector.ProcessSamplesAsync(Pcm(1000, 3)); Assert.Fail("Disposed detectors must reject input."); }
            catch (ObjectDisposedException) { }
        }
    }
}

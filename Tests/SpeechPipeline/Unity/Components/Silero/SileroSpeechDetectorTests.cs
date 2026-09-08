using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;
using ChatdollKit.SpeechPipeline.VAD.Silero;
using NUnit.Framework;
using UnityEngine;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Silero
{
    public class SileroSpeechDetectorTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask BundledSentisModelProcessesPcmAndAppliesLiveSettings(bool stream)
        {
            var owner = new GameObject("Silero Sentis integration test");
            SpeechDetectorLease lease = null;
            var recognizer = new BorrowedRecognizer();
            try
            {
                var component = AddDetector(owner, stream);
                component.SpeechProbabilityThreshold = 1;
                component.UseVolumeThreshold = true;
                component.VolumeDbThreshold = -40;
                lease = await component.CreateDetectorAsync(recognizer, CancellationToken.None);
                Assert.That(lease.Detector, stream ? Is.TypeOf<SileroStreamSpeechDetectorEngine>() : Is.TypeOf<SileroSpeechDetectorEngine>());
                Assert.That(lease.Detector.SampleRate, Is.EqualTo(16000));
                Assert.That(lease.Detector.Channels, Is.EqualTo(1));
                Assert.That(((SileroSpeechDetectorOptions)lease.Detector.GetOptions()).ChunkSize, Is.EqualTo(512));
                if (stream)
                    Assert.That(((SileroStreamSpeechDetectorEngine)lease.Detector).SpeechRecognizer, Is.SameAs(recognizer));
                var errors = new List<Exception>();
                lease.Detector.Error += errors.Add;
                Assert.That(await lease.Detector.ProcessSamplesAsync(new byte[1024]), Is.False);

                component.UseVolumeThreshold = false;
                component.SpeechProbabilityThreshold = 0.75f;
                component.Settings.SilenceDurationThreshold = 0.25f;
                if (stream) ((SileroStreamSpeechDetector)component).SegmentSilenceThreshold = 0.125f;
                await component.ApplyDetectorOptionsAsync(lease.Detector, component.BuildOptions(), CancellationToken.None);
                var actualOptions = (SileroSpeechDetectorOptions)lease.Detector.GetOptions();
                Assert.That(actualOptions.VolumeDbThreshold, Is.Null);
                Assert.That(actualOptions.SpeechProbabilityThreshold, Is.EqualTo(0.75));
                Assert.That(actualOptions.SilenceDurationThreshold, Is.EqualTo(0.25));
                if (stream)
                    Assert.That(((SileroStreamSpeechDetectorOptions)actualOptions).SegmentSilenceThreshold, Is.EqualTo(0.125));
                Assert.That(await lease.Detector.ProcessSamplesAsync(new byte[1024]), Is.False);
                Assert.That(errors, Is.Empty);
                await lease.DisposeAsync();
                await lease.DisposeAsync();
                Assert.That(recognizer.Disposals, Is.Zero, "The pipeline owns the selected STT provider.");
                await ExpectExceptionAsync<ObjectDisposedException>(async () => await lease.Detector.ProcessSamplesAsync(new byte[1024]));
            }
            finally
            {
                if (lease != null) await lease.DisposeAsync();
                recognizer.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestartKeyIncludesIteratorModeAndPrerollButAllowsLiveThresholdEdits(bool stream)
        {
            var owner = new GameObject("Silero restart settings test");
            try
            {
                var component = AddDetector(owner, stream);
                var original = component.GetRestartKey();
                component.UseVadIterator = true;
                Assert.That(component.GetRestartKey(), Is.Not.EqualTo(original));
                component.UseVadIterator = false;
                var originalPreroll = component.Settings.PrerollBufferCount;
                component.Settings.PrerollBufferCount++;
                Assert.That(component.GetRestartKey(), Is.Not.EqualTo(original));
                component.Settings.PrerollBufferCount = originalPreroll;

                component.SpeechProbabilityThreshold = 0.75f;
                component.UseVolumeThreshold = true;
                component.VolumeDbThreshold = -30;
                component.Settings.SilenceDurationThreshold = 0.25f;
                if (stream) ((SileroStreamSpeechDetector)component).SegmentSilenceThreshold = 0.125f;
                Assert.That(component.GetRestartKey(), Is.EqualTo(original), "Threshold edits apply to the current detector.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [TestCase(false, 8000)]
        [TestCase(true, 8000)]
        [TestCase(false, 48000)]
        [TestCase(true, 48000)]
        public async NUnitTask UnsupportedSampleRateIsRejectedWhenBuildingOptionsAndCreatingTheDetector(bool stream, int sampleRate)
        {
            var owner = new GameObject("Silero unsupported format test");
            var recognizer = new BorrowedRecognizer();
            try
            {
                var component = AddDetector(owner, stream);
                component.Settings.SampleRate = sampleRate;
                var optionsError = Assert.Throws<NotSupportedException>(() => component.BuildOptions());
                Assert.That(optionsError.Message, Does.Contain("16000"));
                var creationError = await ExpectExceptionAsync<NotSupportedException>(async () =>
                    await component.CreateDetectorAsync(recognizer, CancellationToken.None));
                Assert.That(creationError.Message, Does.Contain("16000"));
                Assert.That(recognizer.Disposals, Is.Zero);
            }
            finally
            {
                recognizer.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask CanceledCreationCanBeRetriedWithoutDisposingTheBorrowedRecognizer(bool stream)
        {
            var owner = new GameObject("Silero canceled creation test");
            SpeechDetectorLease lease = null;
            var recognizer = new BorrowedRecognizer();
            try
            {
                var component = AddDetector(owner, stream);
                await ExpectExceptionAsync<OperationCanceledException>(async () =>
                    await component.CreateDetectorAsync(recognizer, new CancellationToken(true)));
                Assert.That(recognizer.Disposals, Is.Zero);
                lease = await component.CreateDetectorAsync(recognizer, CancellationToken.None);
                var errors = new List<Exception>();
                lease.Detector.Error += errors.Add;
                Assert.That(await lease.Detector.ProcessSamplesAsync(new byte[1024]), Is.False);
                Assert.That(errors, Is.Empty);
            }
            finally
            {
                if (lease != null) await lease.DisposeAsync();
                recognizer.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask CanceledInputDoesNotPreventFollowingPcmFromBeingProcessed(bool stream)
        {
            var owner = new GameObject("Silero canceled input test");
            SpeechDetectorLease lease = null;
            var recognizer = new BorrowedRecognizer();
            try
            {
                var component = AddDetector(owner, stream);
                lease = await component.CreateDetectorAsync(recognizer, CancellationToken.None);
                var errors = new List<Exception>();
                lease.Detector.Error += errors.Add;
                await ExpectExceptionAsync<OperationCanceledException>(async () =>
                    await lease.Detector.ProcessSamplesAsync(new byte[1024], cancellationToken: new CancellationToken(true)));
                Assert.That(await lease.Detector.ProcessSamplesAsync(new byte[1024]), Is.False);
                Assert.That(errors, Is.Empty);
            }
            finally
            {
                if (lease != null) await lease.DisposeAsync();
                recognizer.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public async NUnitTask StandardDetectorCanBeCreatedWithoutARecognizer()
        {
            var owner = new GameObject("Silero standard recognizer dependency test");
            SpeechDetectorLease lease = null;
            try
            {
                lease = await owner.AddComponent<SileroSpeechDetector>().CreateDetectorAsync(null, CancellationToken.None);
                var errors = new List<Exception>();
                lease.Detector.Error += errors.Add;
                Assert.That(await lease.Detector.ProcessSamplesAsync(new byte[1024]), Is.False);
                Assert.That(errors, Is.Empty);
            }
            finally
            {
                if (lease != null) await lease.DisposeAsync();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public async NUnitTask StreamDetectorRequiresARecognizerBeforeLoadingTheModel()
        {
            var owner = new GameObject("Silero stream recognizer dependency test");
            try
            {
                var component = owner.AddComponent<SileroStreamSpeechDetector>();
                await ExpectExceptionAsync<ArgumentNullException>(async () =>
                    await component.CreateDetectorAsync(null, CancellationToken.None));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        private static SileroSpeechDetector AddDetector(GameObject owner, bool stream)
            => stream ? owner.AddComponent<SileroStreamSpeechDetector>() : owner.AddComponent<SileroSpeechDetector>();

        private sealed class BorrowedRecognizer : ISpeechRecognizer, IDisposable
        {
            public int Disposals;
            public UniTask<SpeechRecognitionResult> RecognizeAsync(string sessionId, byte[] audio, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("Silent input should not request recognition.");
            public void Dispose() => Disposals++;
        }

        private static async UniTask<T> ExpectExceptionAsync<T>(Func<UniTask> operation) where T : Exception
        {
            try { await operation(); }
            catch (Exception error)
            {
                Assert.That(error, Is.InstanceOf<T>());
                return (T)error;
            }
            Assert.Fail("Expected " + typeof(T).Name);
            return null;
        }
    }
}

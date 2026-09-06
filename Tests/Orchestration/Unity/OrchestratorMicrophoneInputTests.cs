using System;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Orchestration;
using NUnit.Framework;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class OrchestratorMicrophoneInputTests
    {
        [Test]
        public void SuppressionDiscardsPartialMessageBeforeForwardingResumes()
        {
            var input = new OrchestratorMicrophoneInput(16000, 1, 16000, 2);
            Assert.That(input.Convert(new[] { 0.5f }, 16000, false), Is.Empty);
            Assert.That(input.Convert(new[] { 0.75f, 0.75f }, 16000, true), Is.Empty);
            var frames = input.Convert(new[] { -0.5f, -0.5f }, 16000, false);
            Assert.That(frames.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(new byte[] { 0, 192, 0, 192 }, frames[0]);
        }

        [Test]
        public async NUnitTask AShortSuppressionBoundaryCanResetFromAnotherThread()
        {
            var input = new OrchestratorMicrophoneInput(16000, 1, 16000, 2);
            input.Convert(new[] { 0.5f }, 16000, false);
            await NUnitTask.Run(input.RequestReset);
            // The suppression interval ended before the next microphone event.
            var frames = input.Convert(new[] { -0.5f, -0.5f }, 16000, false);
            CollectionAssert.AreEqual(new byte[] { 0, 192, 0, 192 }, frames[0]);
        }

        [Test]
        public void SuppressionAlsoDiscardsResamplerInterpolationHistory()
        {
            var input = new OrchestratorMicrophoneInput(8000, 1, 16000, 3);
            input.Convert(new[] { 1f }, 8000, false);
            input.Convert(Array.Empty<float>(), 8000, true);
            var frames = input.Convert(new[] { -0.5f, -0.5f }, 8000, false);
            CollectionAssert.AreEqual(new byte[] { 0, 192, 0, 192, 0, 192 }, frames[0]);
        }

        [Test]
        public void StereoCaptureIsConvertedToMonoPcm16()
        {
            var input = new OrchestratorMicrophoneInput(16000, 2, 16000, 2);
            var frames = input.Convert(new[] { 0.5f, -0.5f, 1f, 0f }, 16000, false);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 64 }, frames[0]);
        }

        [Test]
        public void InputFormatChangesRequireRestartEvenWhileSuppressed()
        {
            var input = new OrchestratorMicrophoneInput(16000, 1, 16000, 2);
            Assert.Throws<InvalidOperationException>(() => input.Convert(new[] { 0f }, 44100, true));
        }
    }
}

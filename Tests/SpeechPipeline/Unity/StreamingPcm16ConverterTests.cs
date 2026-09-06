using System;
using System.Collections.Generic;
using System.Linq;
using ChatdollKit.SpeechPipeline;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline.Unity
{
    public class StreamingPcm16ConverterTests
    {
        [Test]
        public void ResamplingDoesNotDependOnCaptureChunkBoundaries()
        {
            var samples = Enumerable.Range(0, 44101).Select(i => (float)Math.Sin(i * 0.021)).ToArray();
            var whole = new StreamingPcm16Converter(44100, 1, 16000, 512);
            var split = new StreamingPcm16Converter(44100, 1, 16000, 512);
            var expected = whole.Process(samples).SelectMany(x => x).ToArray();
            var actual = new List<byte>();
            for (var i = 0; i < samples.Length;)
            {
                int length = Math.Min(1 + i % 997, samples.Length - i);
                var chunk = new float[length];
                Array.Copy(samples, i, chunk, 0, length);
                actual.AddRange(split.Process(chunk).SelectMany(x => x));
                i += length;
            }
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(split.BufferedSampleCount, Is.EqualTo(whole.BufferedSampleCount));
            Assert.That(actual.Count / 2 + split.BufferedSampleCount, Is.EqualTo(16001));
        }

        [Test]
        public void StereoIsDownmixedClampedAndEncodedLittleEndian()
        {
            var converter = new StreamingPcm16Converter(16000, 2, 16000, 4);
            var message = converter.Process(new float[] { 1, 1, -1, -1, 0.5f, -0.5f, 2, 2 }).Single();
            Assert.That(message, Is.EqualTo(new byte[] { 255, 127, 0, 128, 0, 0, 255, 127 }));
        }

        [Test]
        public void UpsamplingInterpolatesBetweenAdjacentFrames()
        {
            var converter = new StreamingPcm16Converter(2, 1, 4, 5);
            var message = converter.Process(new float[] { -1, 0, 1 }).Single();
            Assert.That(message, Is.EqualTo(new byte[] { 0, 128, 0, 192, 0, 0, 0, 64, 255, 127 }));
        }

        [Test]
        public void ResetDiscardsPartialMessageAndResamplingHistory()
        {
            var converter = new StreamingPcm16Converter(3, 1, 4, 3);
            converter.Process(new float[] { 1, 1 });
            Assert.That(converter.BufferedSampleCount, Is.GreaterThan(0));
            converter.Reset();
            var expected = new StreamingPcm16Converter(3, 1, 4, 3).Process(new float[] { -1, -0.5f, 0, 1 });
            var actual = converter.Process(new float[] { -1, -0.5f, 0, 1 });
            Assert.That(actual.SelectMany(x => x), Is.EqualTo(expected.SelectMany(x => x)));
        }

        [Test]
        public void InvalidInputDoesNotChangeBufferedAudioOrPhase()
        {
            var tested = new StreamingPcm16Converter(3, 2, 4, 3);
            var reference = new StreamingPcm16Converter(3, 2, 4, 3);
            tested.Process(new float[] { 0.5f, 0.5f });
            reference.Process(new float[] { 0.5f, 0.5f });
            Assert.Throws<ArgumentException>(() => tested.Process(new float[] { 0.1f }));
            Assert.Throws<ArgumentException>(() => tested.Process(new float[] { 0.1f, float.NaN }));
            var next = new float[] { 0, 0, -1, -1, 1, 1 };
            Assert.That(tested.Process(next).SelectMany(x => x), Is.EqualTo(reference.Process(next).SelectMany(x => x)));
        }
    }
}

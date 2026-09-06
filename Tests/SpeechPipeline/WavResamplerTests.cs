using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ChatdollKit.SpeechPipeline.TTS;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class WavResamplerTests
    {
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(24)]
        [TestCase(32)]
        public void LinearRampPreservesSampleWidthAndSignedRounding(int bits)
        {
            var source = Wave(new[] { -3, 0, 3 }, 8000, 1, bits);
            var result = WavResampler.ReadWave(WavResampler.Resample(source, 16000));
            Assert.That(result.SampleRate, Is.EqualTo(16000));
            Assert.That(result.BitsPerSample, Is.EqualTo(bits));
            CollectionAssert.AreEqual(bits == 32 ? new[] { -3, -1, 0, 1, 3 } : new[] { -3, -2, 0, 1, 3 }, Samples(result));
        }

        [Test]
        public void StereoChannelsRemainIndependent()
        {
            var result = WavResampler.ReadWave(WavResampler.Resample(Wave(new[] { -300, 300, 0, 0, 300, -300 }, 8000, 2, 16), 16000));
            Assert.That(result.Channels, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { -300, 300, -150, 150, 0, 0, 150, -150, 300, -300 }, Samples(result));
        }

        // Python 3.11 audioop.ratecv, same 64 stereo frames; 8-bit data uses the source's +/-128 bias conversion.
        [TestCase(8, "2ec425f569f3716b7550c33094859162dc636d943a5c33b612a1187800f3c6c3")]
        [TestCase(16, "34fec95cd2442fa6ec9fc2ee0bbb152fbf292558e0aea71a3b7fb0e773d05735")]
        [TestCase(24, "0cb7706538a34cec41594852c98a7ceb10fddd07b600df7ae3c2c4bc89cf7a4b")]
        [TestCase(32, "da260571893f42f35ad6d8ea080cec63e2467fc5254b5bf7c9d4dbce6f2bc292")]
        public void NonIntegralRateMatchesPythonAudioop(int bits, string expectedHash)
        {
            var samples = new int[128];
            for (var i = 0; i < samples.Length; i++) samples[i] = ((i * 37) % 201 - 100) * (1 << (bits - 8));
            var result = WavResampler.ReadWave(WavResampler.Resample(Wave(samples, 44100, 2, bits), 16000));
            using (var hash = SHA256.Create())
                Assert.That(BitConverter.ToString(hash.ComputeHash(result.Audio)).Replace("-", "").ToLowerInvariant(), Is.EqualTo(expectedHash));
        }

        [Test]
        public void DownsampleDoesNotExtendBeyondLastInputFrame()
        {
            var source = new int[48000];
            var result = WavResampler.ReadWave(WavResampler.Resample(Wave(source, 48000, 1, 16), 16000));
            Assert.That(result.Audio.Length / 2, Is.EqualTo(16000));
        }

        [Test]
        public void SameRateReturnsOriginalContainerAndNonWavePassesThrough()
        {
            var wave = Wave(new[] { 1, 2 }, 24000, 1, 16);
            Assert.That(WavResampler.Resample(wave, 24000), Is.SameAs(wave));
            var mp3 = new byte[] { 73, 68, 51, 0, 1, 2 };
            Assert.That(WavResampler.IsWave(mp3), Is.False);
            Assert.That(WavResampler.ResampleIfWave(mp3, 16000), Is.SameAs(mp3));
        }

        [Test]
        public void EmptyAndSingleFrameAudioAreSupported()
        {
            Assert.That(WavResampler.ReadWave(WavResampler.Resample(Wave(Array.Empty<int>(), 24000, 1, 16), 16000)).Audio, Is.Empty);
            CollectionAssert.AreEqual(new[] { 123 }, Samples(WavResampler.ReadWave(WavResampler.Resample(Wave(new[] { 123 }, 24000, 1, 16), 48000))));
        }

        [Test]
        public void ReadWaveSupportsDataBeforeFormatAndUnknownOddChunks()
        {
            var standard = Wave(new[] { -123, 456 }, 24000, 1, 16);
            byte[] reordered;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(standard, 0, 12);
                writer.Write(Encoding.ASCII.GetBytes("JUNK")); writer.Write(1); writer.Write((byte)42); writer.Write((byte)0);
                writer.Write(standard, 36, standard.Length - 36);
                writer.Write(standard, 12, 24);
                reordered = stream.ToArray();
            }
            Put32(reordered, 4, reordered.Length - 8);
            CollectionAssert.AreEqual(new[] { -123, 456 }, Samples(WavResampler.ReadWave(reordered)));
        }

        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void KnownStreamingLengthSentinelsReadToEnd(int sentinel)
        {
            var wave = Wave(new[] { -1, 2, -3 }, 24000, 1, 16);
            Put32(wave, 4, sentinel); Put32(wave, 40, sentinel);
            CollectionAssert.AreEqual(new[] { -1, 2, -3 }, Samples(WavResampler.ReadWave(wave)));
            Assert.That(WavResampler.ReadWave(WavResampler.Resample(wave, 16000)).SampleRate, Is.EqualTo(16000));
        }

        [Test]
        public void ExtensibleIntegerPcmIsAccepted()
        {
            var result = WavResampler.ReadWave(ExtensibleWave());
            Assert.That(result.Channels, Is.EqualTo(2));
            Assert.That(result.BitsPerSample, Is.EqualTo(24));
            CollectionAssert.AreEqual(new[] { -8388608, 8388607 }, Samples(result));
        }

        [Test]
        public void FloatAndPackedExtensiblePcmAreExplicitlyRejected()
        {
            var floating = Wave(new[] { 0 }, 24000, 1, 32); floating[20] = 3;
            Assert.Throws<NotSupportedException>(() => WavResampler.Resample(floating, 16000));
            var packed = ExtensibleWave(); packed[38] = 20;
            Assert.Throws<NotSupportedException>(() => WavResampler.ReadWave(packed));
            var extensibleFloat = ExtensibleWave(); extensibleFloat[44] = 3;
            Assert.Throws<NotSupportedException>(() => WavResampler.ReadWave(extensibleFloat));
        }

        [TestCase(4, 1000)]
        [TestCase(40, 1000)]
        [TestCase(28, 1)]
        [TestCase(32, 1)]
        public void MalformedContainerAndPcmMetadataAreRejected(int offset, int value)
        {
            var wave = Wave(new[] { 1, 2 }, 24000, 1, 16);
            Put32(wave, offset, value);
            Assert.Throws<ArgumentException>(() => WavResampler.ReadWave(wave));
        }

        [Test]
        public void PartialPcmFramesAreRejected()
        {
            var wave = Wave(new[] { 1, 2 }, 24000, 2, 16);
            Put32(wave, 40, 3);
            Assert.Throws<ArgumentException>(() => WavResampler.ReadWave(wave));
        }

        [Test]
        public void CancellationAndTargetRateAreValidatedBeforeProcessing()
        {
            var wave = Wave(new[] { 1, 2 }, 24000, 1, 16);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => WavResampler.Resample(wave, 16000, cancellation.Token));
                Assert.Throws<OperationCanceledException>(() => WavResampler.ResampleIfWave(Array.Empty<byte>(), 16000, cancellation.Token));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => WavResampler.Resample(wave, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => WavResampler.Resample(wave, 768001));
        }

        private static byte[] Wave(int[] samples, int rate, int channels, int bits)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                var size = samples.Length * (bits / 8);
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + size + (size & 1));
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)channels);
                writer.Write(rate); writer.Write(rate * channels * (bits / 8)); writer.Write((ushort)(channels * (bits / 8))); writer.Write((ushort)bits);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size);
                foreach (var sample in samples)
                {
                    if (bits == 8) writer.Write((byte)(sample + 128));
                    else for (var i = 0; i < bits / 8; i++) writer.Write((byte)(sample >> (i * 8)));
                }
                if ((size & 1) != 0) writer.Write((byte)0);
                return stream.ToArray();
            }
        }
        private static int[] Samples(PcmWaveData data)
        {
            var width = data.BitsPerSample / 8;
            var values = new int[data.Audio.Length / width];
            for (var sample = 0; sample < values.Length; sample++)
            {
                if (width == 1) values[sample] = data.Audio[sample] - 128;
                else
                {
                    var value = 0;
                    for (var i = 0; i < width; i++) value |= data.Audio[sample * width + i] << (8 * i);
                    var shift = 32 - data.BitsPerSample;
                    values[sample] = (value << shift) >> shift;
                }
            }
            return values;
        }
        private static byte[] ExtensibleWave()
        {
            var standard = Wave(new[] { -8388608, 8388607 }, 24000, 2, 24);
            var result = new byte[standard.Length + 24];
            Array.Copy(standard, result, 36);
            Array.Copy(standard, 36, result, 60, standard.Length - 36);
            Put32(result, 4, result.Length - 8); Put32(result, 16, 40);
            result[20] = 254; result[21] = 255; result[36] = 22; result[38] = 24; result[40] = 3;
            var pcmGuid = new byte[] { 1, 0, 0, 0, 0, 0, 16, 0, 128, 0, 0, 170, 0, 56, 155, 113 };
            Array.Copy(pcmGuid, 0, result, 44, 16);
            return result;
        }
        private static void Put32(byte[] target, int offset, int value)
        { for (var i = 0; i < 4; i++) target[offset + i] = (byte)(value >> (8 * i)); }
    }
}

using ChatdollKit.Avatar.LipSync;
using System;
using System.IO;
using System.Text;
using System.Threading;
using ChatdollKit.Avatar;
using NUnit.Framework;

namespace ChatdollKit.Tests.Avatar
{
    public class WaveAudioTests
    {
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(24)]
        [TestCase(32)]
        public void WaveDecodingPreservesStereoFrameCountAndFullScale(int bits)
        {
            var decoded = WaveAudio.Decode(Wave(bits));
            Assert.That(decoded.Channels, Is.EqualTo(2));
            Assert.That(decoded.SampleRate, Is.EqualTo(24000));
            Assert.That(decoded.FrameCount, Is.EqualTo(2));
            Assert.That(decoded.Samples.Length, Is.EqualTo(4));
            Assert.That(decoded.Samples[0], Is.EqualTo(-1));
            Assert.That(decoded.Samples[1], Is.EqualTo((float)(1 - 1 / Math.Pow(2, bits - 1))).Within(1e-7));
            Assert.That(decoded.Samples[2], Is.EqualTo(0));
            Assert.That(decoded.Samples[3], Is.EqualTo(0.5));
        }

        [Test]
        public void WaveDecodingRejectsCompressedDataAndObservesCancellation()
        {
            Assert.Throws<ArgumentException>(() => WaveAudio.Decode(Encoding.ASCII.GetBytes("ID3-not-a-wave")));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => WaveAudio.Decode(Wave(16), cancellation.Token));
            }
        }

        private static byte[] Wave(int bits)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                var width = bits / 8;
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + width * 4);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)2);
                writer.Write(24000); writer.Write(24000 * width * 2); writer.Write((ushort)(width * 2)); writer.Write((ushort)bits);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(width * 4);
                var halfRange = 1L << (bits - 1);
                foreach (var value in new[] { -halfRange, halfRange - 1, 0, halfRange / 2 })
                {
                    var sample = bits == 8 ? value + 128 : value;
                    for (var index = 0; index < width; index++) writer.Write((byte)(sample >> (index * 8)));
                }
                return stream.ToArray();
            }
        }
    }
}

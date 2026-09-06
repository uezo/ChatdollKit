using System;
using System.IO;
using System.Text;
using ChatdollKit.SpeechPipeline.STT;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class Pcm16AudioTests
    {
        [TestCase(8000)]
        [TestCase(16000)]
        [TestCase(48000)]
        public void WritesCanonicalMonoPcm16Wave(int sampleRate)
        {
            var pcm = new byte[] { 0, 128, 255, 127, 0, 0 };
            var wave = Pcm16Audio.WriteWave(pcm, sampleRate);
            using (var reader = new BinaryReader(new MemoryStream(wave)))
            {
                Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("RIFF"));
                Assert.That(reader.ReadInt32(), Is.EqualTo(36 + pcm.Length));
                Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(8)), Is.EqualTo("WAVEfmt "));
                Assert.That(reader.ReadInt32(), Is.EqualTo(16));
                Assert.That(reader.ReadInt16(), Is.EqualTo(1));
                Assert.That(reader.ReadInt16(), Is.EqualTo(1));
                Assert.That(reader.ReadInt32(), Is.EqualTo(sampleRate));
                Assert.That(reader.ReadInt32(), Is.EqualTo(sampleRate * 2));
                Assert.That(reader.ReadInt16(), Is.EqualTo(2));
                Assert.That(reader.ReadInt16(), Is.EqualTo(16));
                Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("data"));
                Assert.That(reader.ReadInt32(), Is.EqualTo(pcm.Length));
                CollectionAssert.AreEqual(pcm, reader.ReadBytes(pcm.Length));
            }
            var decoded = Pcm16Audio.ReadWave(wave);
            Assert.That(decoded.SampleRate, Is.EqualTo(sampleRate));
            CollectionAssert.AreEqual(pcm, decoded.Audio);
        }

        [Test]
        public void ReadsAdditionalRiffChunksWithOddBytePadding()
        {
            var wave = Pcm16Audio.WriteWave(new byte[] { 1, 2 }, 16000);
            var extended = new byte[wave.Length + 10];
            Array.Copy(wave, 0, extended, 0, 12);
            Array.Copy(Encoding.ASCII.GetBytes("JUNK"), 0, extended, 12, 4);
            extended[16] = 1;
            extended[20] = 255;
            Array.Copy(wave, 12, extended, 22, wave.Length - 12);
            Array.Copy(BitConverter.GetBytes(extended.Length - 8), 0, extended, 4, 4);
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, Pcm16Audio.ReadWave(extended).Audio);
        }

        [TestCase(20, 3)] // IEEE float
        [TestCase(22, 2)] // stereo
        [TestCase(32, 4)] // wrong block alignment
        [TestCase(34, 8)] // 8-bit audio
        [TestCase(4, 255)] // truncated RIFF
        [TestCase(40, 255)] // truncated data
        public void RejectsUnsupportedOrTruncatedWave(int offset, byte value)
        {
            var wave = Pcm16Audio.WriteWave(new byte[] { 0, 0 }, 16000);
            wave[offset] = value;
            Assert.Throws<ArgumentException>(() => Pcm16Audio.ReadWave(wave));
        }

        [Test]
        public void RejectsIncompleteSamplesBeforeWriting()
            => Assert.Throws<ArgumentException>(() => Pcm16Audio.WriteWave(new byte[] { 0 }, 16000));
    }
}

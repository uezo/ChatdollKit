using System;
using System.IO;
using System.Text;

namespace ChatdollKit.SpeechPipeline.STT
{
    public sealed class Pcm16Wave
    {
        public byte[] Audio { get; }
        public int SampleRate { get; }
        internal Pcm16Wave(byte[] audio, int sampleRate) { Audio = audio; SampleRate = sampleRate; }
    }

    /// <summary>Mono PCM16 little-endian WAV framing. No resampling or channel conversion.</summary>
    public static class Pcm16Audio
    {
        public static byte[] WriteWave(byte[] audio, int sampleRate)
        {
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            if (audio.Length % 2 != 0) throw new ArgumentException("PCM16 must contain complete samples.", nameof(audio));
            if (sampleRate <= 0 || sampleRate > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            using (var stream = new MemoryStream(checked(44 + audio.Length)))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(checked(36 + audio.Length));
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(audio.Length);
                writer.Write(audio);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Reads an uncompressed mono PCM16 WAV, allowing additional RIFF chunks.</summary>
        public static Pcm16Wave ReadWave(byte[] wave)
        {
            if (wave == null) throw new ArgumentNullException(nameof(wave));
            using (var stream = new MemoryStream(wave, false))
            using (var reader = new BinaryReader(stream, Encoding.ASCII))
            {
                if (wave.Length < 12 || Tag(reader) != "RIFF") throw InvalidWave();
                var end = (long)reader.ReadUInt32() + 8;
                if (end < 12 || end > wave.Length || Tag(reader) != "WAVE") throw InvalidWave();
                int sampleRate = 0;
                byte[] audio = null;
                while (stream.Position < end)
                {
                    if (end - stream.Position < 8) throw InvalidWave();
                    var tag = Tag(reader);
                    var size = reader.ReadUInt32();
                    var next = stream.Position + size + (size & 1);
                    if (next > end) throw InvalidWave();
                    if (tag == "fmt ")
                    {
                        if (size < 16 || sampleRate != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 1)
                            throw InvalidWave();
                        sampleRate = reader.ReadInt32();
                        var byteRate = reader.ReadUInt32();
                        var blockAlign = reader.ReadUInt16();
                        var bits = reader.ReadUInt16();
                        if (sampleRate <= 0 || (long)sampleRate * 2 != byteRate || blockAlign != 2 || bits != 16)
                            throw InvalidWave();
                    }
                    else if (tag == "data")
                    {
                        if (audio != null || size > int.MaxValue || size % 2 != 0) throw InvalidWave();
                        audio = reader.ReadBytes((int)size);
                    }
                    stream.Position = next;
                }
                if (sampleRate == 0 || audio == null) throw InvalidWave();
                return new Pcm16Wave(audio, sampleRate);
            }
        }

        private static string Tag(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
        private static ArgumentException InvalidWave() => new ArgumentException("Expected a complete RIFF WAV containing uncompressed mono PCM16 audio.");
    }
}

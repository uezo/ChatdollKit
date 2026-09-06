// Adapted to C# from AIAvatarKit's WAV postprocessor (uezo, Apache-2.0), revision d775070.
using System;
using System.Threading;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public sealed class PcmWaveData
    {
        public byte[] Audio { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public int BitsPerSample { get; }
        internal PcmWaveData(byte[] audio, int sampleRate, int channels, int bitsPerSample)
        { Audio = audio; SampleRate = sampleRate; Channels = channels; BitsPerSample = bitsPerSample; }
    }

    /// <summary>Linear PCM WAV conversion matching audioop.ratecv's frame timing and integer sample conversion.
    /// Channels and sample width are preserved. Resampling writes a canonical WAV without ancillary chunks.</summary>
    public static class WavResampler
    {
        public static bool IsWave(byte[] audio) => audio != null && audio.Length >= 12 && Tag(audio, 0, "RIFF") && Tag(audio, 8, "WAVE");

        public static byte[] ResampleIfWave(byte[] audio, int targetSampleRate, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return IsWave(audio) ? Resample(audio, targetSampleRate, token) : audio;
        }

        public static PcmWaveData ReadWave(byte[] wave) => ReadWaveCore(wave, CancellationToken.None);

        private static PcmWaveData ReadWaveCore(byte[] wave, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!IsWave(wave)) throw new ArgumentException("Expected a RIFF WAVE file.", nameof(wave));
            var declaredSize = U32(wave, 4);
            var end = IsStreamingSize(declaredSize) ? wave.Length : checked((long)declaredSize + 8);
            if (end < 12 || end > wave.Length) throw InvalidWave("Truncated RIFF container.");
            int sampleRate = 0, channels = 0, bits = 0, dataOffset = -1, dataLength = 0;
            var foundFormat = false;
            for (long position = 12; position < end;)
            {
                token.ThrowIfCancellationRequested();
                if (end - position < 8) throw InvalidWave("Truncated chunk header.");
                var offset = (int)position;
                var size = U32(wave, offset + 4);
                var isData = Tag(wave, offset, "data");
                var body = position + 8;
                var streamingData = isData && IsStreamingSize(size);
                var length = streamingData ? end - body : size;
                if (length > end - body) throw InvalidWave("Truncated WAV chunk.");
                if (Tag(wave, offset, "fmt "))
                {
                    if (foundFormat || length < 16) throw InvalidWave("Invalid format chunk.");
                    foundFormat = true;
                    var start = (int)body;
                    var format = U16(wave, start);
                    channels = U16(wave, start + 2);
                    var rate = U32(wave, start + 4);
                    var byteRate = U32(wave, start + 8);
                    var alignment = U16(wave, start + 12);
                    bits = U16(wave, start + 14);
                    if (format == 0xfffe)
                    {
                        // WAVE_FORMAT_EXTENSIBLE is accepted only for full-width integer PCM.
                        if (length < 40 || U16(wave, start + 16) < 22 || U16(wave, start + 16) > length - 18)
                            throw InvalidWave("Invalid extensible format chunk.");
                        var validBits = U16(wave, start + 18);
                        var pcmGuid = new byte[] { 1, 0, 0, 0, 0, 0, 16, 0, 128, 0, 0, 170, 0, 56, 155, 113 };
                        for (var i = 0; i < pcmGuid.Length; i++)
                            if (wave[start + 24 + i] != pcmGuid[i]) throw new NotSupportedException("WAV conversion requires integer PCM audio.");
                        if (validBits != 0 && validBits != bits) throw new NotSupportedException("Packed valid-bit WAV samples are not supported.");
                    }
                    else if (format != 1) throw new NotSupportedException("WAV conversion requires uncompressed integer PCM audio.");
                    if (channels == 0 || rate == 0 || rate > 768000 || (bits != 8 && bits != 16 && bits != 24 && bits != 32))
                        throw InvalidWave("Invalid PCM format.");
                    sampleRate = (int)rate;
                    if (alignment != channels * (bits / 8) || byteRate != (long)sampleRate * alignment)
                        throw InvalidWave("Inconsistent PCM block alignment or byte rate.");
                }
                else if (isData)
                {
                    if (dataOffset >= 0) throw InvalidWave("Multiple data chunks are not supported.");
                    dataOffset = (int)body;
                    dataLength = (int)length;
                }
                if (streamingData) break;
                position = body + length + (length & 1);
                // Some encoders omit the final pad byte. It cannot conceal another chunk.
                if (position > end && body + length != end) throw InvalidWave("Truncated chunk padding.");
            }
            if (!foundFormat || dataOffset < 0) throw InvalidWave("WAV requires fmt and data chunks.");
            if (dataLength % (channels * (bits / 8)) != 0) throw InvalidWave("WAV ends in a partial PCM frame.");
            var audio = new byte[dataLength];
            Buffer.BlockCopy(wave, dataOffset, audio, 0, dataLength);
            token.ThrowIfCancellationRequested();
            return new PcmWaveData(audio, sampleRate, channels, bits);
        }

        public static byte[] Resample(byte[] wave, int targetSampleRate, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (targetSampleRate < 1 || targetSampleRate > 768000) throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
            var source = ReadWaveCore(wave, token);
            if (source.SampleRate == targetSampleRate) return wave;
            var width = source.BitsPerSample / 8;
            var blockSize = width * source.Channels;
            var frames = source.Audio.Length / blockSize;
            // audioop emits the first input frame at t=0 and never extrapolates beyond the last frame.
            var outputFrames = frames == 0 ? 0 : ((long)frames - 1) * targetSampleRate / source.SampleRate + 1;
            var audioLength = outputFrames * blockSize;
            if (audioLength > int.MaxValue - 45) throw new ArgumentException("Resampled WAV exceeds the supported byte array size.", nameof(wave));
            var result = new byte[44 + (int)audioLength + ((int)audioLength & 1)];
            WriteHeader(result, (int)audioLength, targetSampleRate, source.Channels, source.BitsPerSample);
            for (long frame = 0; frame < outputFrames; frame++)
            {
                if ((frame & 1023) == 0) token.ThrowIfCancellationRequested();
                var position = frame * source.SampleRate;
                var left = (int)(position / targetSampleRate);
                var fraction = position % targetSampleRate;
                var right = Math.Min(left + 1, frames - 1);
                for (var channel = 0; channel < source.Channels; channel++)
                {
                    var first = ReadSample(source.Audio, left * blockSize + channel * width, width);
                    var second = ReadSample(source.Audio, right * blockSize + channel * width, width);
                    // audioop promotes narrow samples to signed 32-bit precision before interpolation.
                    var shift = 32 - source.BitsPerSample;
                    var value = (((long)first << shift) * (targetSampleRate - fraction) + ((long)second << shift) * fraction) / targetSampleRate;
                    WriteSample(result, 44 + (int)frame * blockSize + channel * width, (int)value >> shift, width);
                }
            }
            token.ThrowIfCancellationRequested();
            return result;
        }

        private static int ReadSample(byte[] audio, int offset, int width)
        {
            if (width == 1) return audio[offset] - 128;
            var value = 0;
            for (var i = 0; i < width; i++) value |= audio[offset + i] << (8 * i);
            var shift = 32 - width * 8;
            return (value << shift) >> shift;
        }
        private static void WriteSample(byte[] audio, int offset, int value, int width)
        {
            if (width == 1) { audio[offset] = (byte)(value + 128); return; }
            for (var i = 0; i < width; i++) audio[offset + i] = (byte)(value >> (8 * i));
        }
        private static void WriteHeader(byte[] result, int dataLength, int sampleRate, int channels, int bits)
        {
            var byteRate = (long)sampleRate * channels * (bits / 8);
            if (byteRate > uint.MaxValue) throw new ArgumentException("The target PCM byte rate exceeds the WAV format limit.", nameof(sampleRate));
            SetTag(result, 0, "RIFF"); Put32(result, 4, result.Length - 8); SetTag(result, 8, "WAVE");
            SetTag(result, 12, "fmt "); Put32(result, 16, 16); Put16(result, 20, 1); Put16(result, 22, channels);
            Put32(result, 24, sampleRate); Put32(result, 28, unchecked((int)byteRate));
            Put16(result, 32, channels * (bits / 8)); Put16(result, 34, bits); SetTag(result, 36, "data"); Put32(result, 40, dataLength);
        }
        private static bool IsStreamingSize(uint size) => size == uint.MaxValue || size == int.MaxValue;
        private static bool Tag(byte[] value, int offset, string tag)
        { for (var i = 0; i < 4; i++) if (value[offset + i] != tag[i]) return false; return true; }
        private static void SetTag(byte[] value, int offset, string tag)
        { for (var i = 0; i < 4; i++) value[offset + i] = (byte)tag[i]; }
        private static int U16(byte[] value, int offset) => value[offset] | (value[offset + 1] << 8);
        private static uint U32(byte[] value, int offset) => (uint)(value[offset] | value[offset + 1] << 8 | value[offset + 2] << 16 | value[offset + 3] << 24);
        private static void Put16(byte[] value, int offset, int number) { value[offset] = (byte)number; value[offset + 1] = (byte)(number >> 8); }
        private static void Put32(byte[] value, int offset, int number)
        { for (var i = 0; i < 4; i++) value[offset + i] = (byte)(number >> (8 * i)); }
        private static ArgumentException InvalidWave(string message) => new ArgumentException(message, "wave");
    }
}

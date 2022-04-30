using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ChatdollKit.IO
{
    public static class AudioConverter
    {
        public static string AudioClipToBase64(AudioClip audioClip, float[] samplingData = null)
        {
            return Convert.ToBase64String(AudioClipToPCM(audioClip, samplingData));
        }

        public static byte[] AudioClipToPCM(AudioClip audioClip, float[] samplingData = null)
        {
            var headerLength = 44;
            var pcm = new byte[audioClip.samples * audioClip.channels * 2 + headerLength];

            // Set header
            SetWaveHeader(pcm, audioClip.channels, audioClip.frequency);

            if (samplingData == null)
            {
                // Try to get sampling data from audio clip. This will not work on WebGL platform
                samplingData = new float[audioClip.samples * audioClip.channels];
                audioClip.GetData(samplingData, 0);
            }

            for (var i = 0; i < samplingData.Length; i++)
            {
                // float to 16bit int to bytes
                Array.Copy(BitConverter.GetBytes((short)(samplingData[i] * 32767)), 0, pcm, i * 2 + headerLength, 2);
            }

            return pcm;
        }

        public static AudioClip PCMToAudioClip(byte[] pcm, string name = "AudioClip from PCM", bool searchDataChunk = false)
        {
            // Get wave info
            var channels = BitConverter.ToUInt16(pcm, 22);
            var frequency = BitConverter.ToInt32(pcm, 24);
            var ckIDPosition = searchDataChunk ? PatternAt(pcm, Encoding.ASCII.GetBytes("data")) : 36;
            var sampleLength = BitConverter.ToInt32(pcm, ckIDPosition + 4) / 2;

            // Convert to sample data
            var samples = new float[sampleLength];
            var headerLength = ckIDPosition + 8;    // ckID 4 + len 4
            for (var i = 0; i < sampleLength; i++)
            {
                samples[i] = (float)BitConverter.ToInt16(pcm, i * 2 + headerLength) / UInt16.MaxValue;
            }

            // Create AudioClip
            var audioClip = AudioClip.Create(name, sampleLength, channels, frequency, false);
            audioClip.SetData(samples, 0);
            return audioClip;
        }

        public static AudioClip PCMToAudioClip(byte[] pcmWithoutHeader, int channels, int frequency, string name = "AudioClip from PCM")
        {
            var headerLength = 44;
            var pcm = new byte[pcmWithoutHeader.Length + headerLength];

            // Set header
            SetWaveHeader(pcm, channels, frequency);

            // Set data
            Array.Copy(pcmWithoutHeader, 0, pcm, headerLength, pcmWithoutHeader.Length);

            // Convert to sample data
            var sampleLength = pcmWithoutHeader.Length / 2;
            var samples = new float[sampleLength];
            for (var i = 0; i < sampleLength; i++)
            {
                samples[i] = (float)BitConverter.ToInt16(pcm, i * 2 + headerLength) / UInt16.MaxValue;
            }

            // Create AudioClip
            var audioClip = AudioClip.Create(name, sampleLength, channels, frequency, false);
            audioClip.SetData(samples, 0);
            return audioClip;
        }

        private static void SetWaveHeader(byte[] pcm, int channels, int frequency)
        {
            Array.Copy(Encoding.ASCII.GetBytes("RIFF"), 0, pcm, 0, 4);
            Array.Copy(BitConverter.GetBytes((UInt32)(pcm.Length - 8)), 0, pcm, 4, 4);
            Array.Copy(Encoding.ASCII.GetBytes("WAVE"), 0, pcm, 8, 4);
            Array.Copy(Encoding.ASCII.GetBytes("fmt "), 0, pcm, 12, 4);
            Array.Copy(BitConverter.GetBytes(16), 0, pcm, 16, 4);
            Array.Copy(BitConverter.GetBytes((UInt16)1), 0, pcm, 20, 2);
            Array.Copy(BitConverter.GetBytes((UInt16)channels), 0, pcm, 22, 2);
            Array.Copy(BitConverter.GetBytes((UInt32)frequency), 0, pcm, 24, 4);
            Array.Copy(BitConverter.GetBytes((UInt32)frequency * 2), 0, pcm, 28, 4);
            Array.Copy(BitConverter.GetBytes((UInt16)2), 0, pcm, 32, 2);
            Array.Copy(BitConverter.GetBytes((UInt16)16), 0, pcm, 34, 2);
            Array.Copy(Encoding.ASCII.GetBytes("data"), 0, pcm, 36, 4);
            Array.Copy(BitConverter.GetBytes((UInt32)(pcm.Length - 44)), 0, pcm, 40, 4);
        }

        // Some voice generator returns the wave with variable length headers :(
        public static int PatternAt(byte[] source, byte[] pattern)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (source.Skip(i).Take(pattern.Length).SequenceEqual(pattern))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}

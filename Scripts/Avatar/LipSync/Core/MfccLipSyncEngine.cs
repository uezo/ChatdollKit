// Adapted from AIAvatarKit's mfcc-lipsync.js (Copyright (c) 2023 uezo,
// Apache-2.0). This C# port adds reusable buffers and interleaved PCM input.
// MFCC processing follows uLipSync v3: https://github.com/hecomi/uLipSync
// Copyright (c) 2021 hecomi, MIT License; see THIRD-PARTY-NOTICES.txt.
using System;
using System.Collections.Generic;

namespace ChatdollKit.Avatar.LipSync
{
    /// <summary>
    /// Analyzes the PCM window ending at a playback position. All positions count
    /// frames per channel. Instances reuse their working buffers and are not thread safe.
    /// </summary>
    public sealed class MfccLipSyncEngine
    {
        // JavaScript Number.EPSILON is not System.Double.Epsilon.
        private const double NumberEpsilon = 2.220446049250313e-16;
        private const double SilenceThreshold = 1e-12;
        private readonly int sampleCount;
        private readonly int targetSampleRate;
        private readonly int mfccCount;
        private readonly int compareMethod;
        private readonly double[] means;
        private readonly double[] standardDeviations;
        private readonly double[][] averages;
        private readonly int[] entryGroups;
        private readonly Viseme[] groupVisemes;
        private readonly double[] scores;
        private readonly double[] groupRatios;
        private readonly double[] visemeWeights = new double[6];
        private readonly double[] data;
        private readonly double[] hammingWindow;
        private readonly double[] fftReal;
        private readonly double[] fftImag;
        private readonly double[] fftTwiddleReal;
        private readonly double[] fftTwiddleImag;
        private readonly int[] fftReversedIndices;
        private readonly double[] spectrum;
        private readonly MelBand[] melBands;
        private readonly double[] melSpectrum;
        private readonly double[][] dctWeights;
        private readonly double[] mfcc;
        private readonly Dictionary<int, RateBuffers> rates = new Dictionary<int, RateBuffers>();
        private double minVolume = -2.5;
        private double maxVolume = -1.5;
        private double volumeGain = 1;

        public double MinVolume
        {
            get => minVolume;
            set { RequireFinite(value, nameof(MinVolume)); minVolume = value; }
        }

        public double MaxVolume
        {
            get => maxVolume;
            set { RequireFinite(value, nameof(MaxVolume)); maxVolume = value; }
        }

        public double VolumeGain
        {
            get => volumeGain;
            set
            {
                RequireFinite(value, nameof(VolumeGain));
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(VolumeGain));
                volumeGain = value;
            }
        }

        public MfccLipSyncEngine(MfccProfile profile)
        {
            ValidateProfile(profile);
            sampleCount = profile.sampleCount;
            targetSampleRate = profile.targetSampleRate;
            mfccCount = profile.mfccNum;
            compareMethod = profile.compareMethod;
            means = new double[mfccCount];
            standardDeviations = new double[mfccCount];
            averages = new double[profile.mfccs.Length][];
            entryGroups = new int[profile.mfccs.Length];
            scores = new double[profile.mfccs.Length];
            var groups = new Dictionary<string, int>(StringComparer.Ordinal);
            var visemes = new List<Viseme>();
            int calibrationCount = 0;
            for (int entryIndex = 0; entryIndex < profile.mfccs.Length; entryIndex++)
            {
                var entry = profile.mfccs[entryIndex];
                if (!groups.TryGetValue(entry.name, out int group))
                {
                    group = groups.Count;
                    groups.Add(entry.name, group);
                    visemes.Add(ParseViseme(entry.name));
                }
                entryGroups[entryIndex] = group;
                var average = new double[mfccCount];
                averages[entryIndex] = average;
                var vectors = entry.mfccCalibrationDataList;
                if (vectors == null || vectors.Length == 0) continue;
                int averageStart = Math.Max(0, vectors.Length - profile.mfccDataCount);
                for (int index = 0; index < vectors.Length; index++)
                {
                    var vector = vectors[index].array;
                    for (int coefficient = 0; coefficient < mfccCount; coefficient++)
                    {
                        if (index >= averageStart) average[coefficient] += vector[coefficient];
                        if (profile.useStandardization) means[coefficient] += vector[coefficient];
                    }
                    calibrationCount++;
                }
                for (int coefficient = 0; coefficient < mfccCount; coefficient++)
                    average[coefficient] /= vectors.Length - averageStart;
            }
            groupVisemes = visemes.ToArray();
            groupRatios = new double[groupVisemes.Length];
            for (int coefficient = 0; coefficient < mfccCount; coefficient++)
                standardDeviations[coefficient] = 1;
            if (profile.useStandardization && calibrationCount > 0)
            {
                for (int coefficient = 0; coefficient < mfccCount; coefficient++)
                {
                    means[coefficient] /= calibrationCount;
                    standardDeviations[coefficient] = 0;
                }
                foreach (var entry in profile.mfccs)
                {
                    if (entry.mfccCalibrationDataList == null) continue;
                    foreach (var vector in entry.mfccCalibrationDataList)
                    {
                        for (int coefficient = 0; coefficient < mfccCount; coefficient++)
                        {
                            double delta = vector.array[coefficient] - means[coefficient];
                            standardDeviations[coefficient] += delta * delta;
                        }
                    }
                }
                for (int coefficient = 0; coefficient < mfccCount; coefficient++)
                {
                    double deviation = Math.Sqrt(standardDeviations[coefficient] / calibrationCount);
                    standardDeviations[coefficient] = deviation > SilenceThreshold ? deviation : 1;
                }
            }

            data = new double[sampleCount];
            mfcc = new double[mfccCount];
            hammingWindow = new double[sampleCount];
            fftReal = new double[sampleCount];
            fftImag = new double[sampleCount];
            fftTwiddleReal = new double[sampleCount];
            fftTwiddleImag = new double[sampleCount];
            fftReversedIndices = new int[sampleCount];
            spectrum = new double[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                hammingWindow[index] = 0.54 - 0.46 * Math.Cos(2 * Math.PI * index / Math.Max(1, sampleCount - 1));
                int reversed = 0;
                for (int source = index, count = sampleCount; count > 1; count >>= 1, source >>= 1)
                    reversed = (reversed << 1) | (source & 1);
                fftReversedIndices[index] = reversed;
            }
            for (int half = 1; half < sampleCount; half *= 2)
            {
                double angle = -2 * Math.PI / (half * 2);
                double stepReal = Math.Cos(angle), stepImag = Math.Sin(angle);
                double real = 1, imaginary = 0;
                for (int index = 0; index < half; index++)
                {
                    fftTwiddleReal[half + index] = real;
                    fftTwiddleImag[half + index] = imaginary;
                    double nextReal = real * stepReal - imaginary * stepImag;
                    imaginary = real * stepImag + imaginary * stepReal;
                    real = nextReal;
                }
            }
            melBands = CreateMelBands(profile.melFilterBankChannels);
            melSpectrum = new double[melBands.Length];
            dctWeights = new double[mfccCount][];
            double dctScale = Math.PI / melBands.Length;
            for (int coefficient = 0; coefficient < mfccCount; coefficient++)
            {
                dctWeights[coefficient] = new double[melBands.Length];
                for (int band = 0; band < melBands.Length; band++)
                    dctWeights[coefficient][band] = Math.Cos((band + 0.5) * (coefficient + 1) * dctScale);
            }
        }

        public LipSyncResult Process(float[] samples, int channels, int sampleRate,
            int samplePosition, double gain = 1, double timeOffsetSeconds = 0)
        {
            double rawVolume = Analyze(samples, channels, sampleRate, samplePosition, timeOffsetSeconds);
            if (rawVolume <= SilenceThreshold) return default;
            if (!IsFinite(gain)) gain = 1;
            double scaledVolume = rawVolume * Math.Max(0, gain) * VolumeGain;
            double volume = scaledVolume > 0
                ? Clamp((Math.Log10(scaledVolume) - MinVolume) / Math.Max(1e-4, MaxVolume - MinVolume), 0, 1)
                : 0;
            double scoreSum = 0;
            for (int entry = 0; entry < averages.Length; entry++)
            {
                double score = Score(averages[entry]);
                scores[entry] = IsFinite(score) && score > 0 ? Math.Min(1, score) : 0;
                scoreSum += scores[entry];
            }
            // No usable classification must not open the first profile vowel by default.
            if (scoreSum <= 0) return new LipSyncResult(0, 0, 0, 0, 0, Viseme.None, 0, rawVolume);
            Array.Clear(groupRatios, 0, groupRatios.Length);
            Array.Clear(visemeWeights, 0, visemeWeights.Length);
            for (int entry = 0; entry < scores.Length; entry++)
                groupRatios[entryGroups[entry]] += scores[entry] / scoreSum;
            int mainGroup = 0;
            double mainRatio = -1;
            for (int group = 0; group < groupRatios.Length; group++)
            {
                double ratio = groupRatios[group];
                visemeWeights[(int)groupVisemes[group]] += ratio * volume;
                if (ratio > mainRatio) { mainGroup = group; mainRatio = ratio; }
            }
            var main = groupVisemes[mainGroup];
            return new LipSyncResult(visemeWeights[1], visemeWeights[2], visemeWeights[3],
                visemeWeights[4], visemeWeights[5], main, volume, rawVolume);
        }

        public void ExtractMfcc(float[] samples, int channels, int sampleRate,
            int samplePosition, double[] destination, double timeOffsetSeconds = 0)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination.Length < mfccCount)
                throw new ArgumentException("The destination has too few MFCC elements.", nameof(destination));
            Analyze(samples, channels, sampleRate, samplePosition, timeOffsetSeconds);
            Array.Copy(mfcc, destination, mfccCount);
        }

        private double Analyze(float[] samples, int channels, int sampleRate, int samplePosition, double timeOffsetSeconds)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            if (samples.Length % channels != 0)
                throw new ArgumentException("The interleaved PCM length must be a multiple of the channel count.", nameof(samples));
            if (sampleRate < targetSampleRate)
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "The input sample rate must be at least the profile target sample rate; upsampling is not supported.");
            RequireFinite(timeOffsetSeconds, nameof(timeOffsetSeconds));
            if (!rates.TryGetValue(sampleRate, out var buffers))
            {
                buffers = new RateBuffers(sampleRate, targetSampleRate, sampleCount);
                rates.Add(sampleRate, buffers);
            }
            int end = (int)Clamp(Math.Floor(samplePosition + timeOffsetSeconds * sampleRate), 0, samples.Length / channels);
            int start = end - buffers.Input.Length;
            Array.Clear(buffers.Input, 0, buffers.Input.Length);
            double sumSquares = 0;
            for (int frame = Math.Max(0, start); frame < end; frame++)
            {
                double mono = 0;
                int sampleIndex = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    double value = samples[sampleIndex + channel];
                    if (IsFinite(value)) mono += value;
                }
                mono /= channels;
                buffers.Input[frame - start] = mono;
                sumSquares += mono * mono;
            }
            double rawVolume = Math.Sqrt(sumSquares / buffers.Input.Length);
            if (rawVolume <= SilenceThreshold)
            {
                Array.Clear(mfcc, 0, mfcc.Length);
                return rawVolume;
            }
            Array.Copy(buffers.Input, buffers.Filtered, buffers.Input.Length);
            // Preserve uLipSync's original + convolution behavior, including at 16 kHz.
            for (int tap = 0; tap < buffers.Filter.Length; tap++)
            {
                double coefficient = buffers.Filter[tap];
                for (int index = tap; index < buffers.Input.Length; index++)
                    buffers.Filtered[index] += coefficient * buffers.Input[index - tap];
            }
            for (int index = 0; index < sampleCount; index++)
                data[index] = buffers.Filtered[buffers.SourceIndices[index]];
            // Backward traversal retains the unmodified previous sample without a copy.
            for (int index = sampleCount - 1; index > 0; index--)
                data[index] -= 0.97 * data[index - 1];
            double max = 0;
            for (int index = 0; index < sampleCount; index++)
            {
                data[index] *= hammingWindow[index];
                max = Math.Max(max, Math.Abs(data[index]));
            }
            if (max > NumberEpsilon)
                for (int index = 0; index < sampleCount; index++) data[index] /= max;
            TransformFft();
            for (int band = 0; band < melBands.Length; band++)
            {
                var filter = melBands[band];
                double energy = 0;
                for (int index = 0; index < filter.Weights.Length; index++)
                    energy += filter.Weights[index] * spectrum[filter.Start + index];
                melSpectrum[band] = 10 * Math.Log10(Math.Max(energy, 1e-30));
            }
            for (int coefficient = 0; coefficient < mfccCount; coefficient++)
            {
                double value = 0;
                for (int band = 0; band < melSpectrum.Length; band++)
                    value += melSpectrum[band] * dctWeights[coefficient][band];
                mfcc[coefficient] = value;
            }
            return rawVolume;
        }

        private void TransformFft()
        {
            Array.Clear(fftImag, 0, fftImag.Length);
            for (int index = 0; index < sampleCount; index++)
                fftReal[fftReversedIndices[index]] = data[index];
            for (int half = 1; half < sampleCount; half *= 2)
            {
                int length = half * 2;
                for (int offset = 0; offset < sampleCount; offset += length)
                {
                    for (int index = 0; index < half; index++)
                    {
                        int even = offset + index, odd = even + half;
                        double twiddleReal = fftTwiddleReal[half + index];
                        double twiddleImag = fftTwiddleImag[half + index];
                        double oddReal = fftReal[odd] * twiddleReal - fftImag[odd] * twiddleImag;
                        double oddImag = fftReal[odd] * twiddleImag + fftImag[odd] * twiddleReal;
                        fftReal[odd] = fftReal[even] - oddReal;
                        fftImag[odd] = fftImag[even] - oddImag;
                        fftReal[even] += oddReal;
                        fftImag[even] += oddImag;
                    }
                }
            }
            for (int index = 0; index < sampleCount; index++)
                spectrum[index] = Math.Sqrt(fftReal[index] * fftReal[index] + fftImag[index] * fftImag[index]);
        }

        private double Score(double[] phoneme)
        {
            if (compareMethod == 2)
            {
                double product = 0, mfccNorm = 0, phonemeNorm = 0;
                for (int index = 0; index < mfccCount; index++)
                {
                    double x = (mfcc[index] - means[index]) / standardDeviations[index];
                    double y = (phoneme[index] - means[index]) / standardDeviations[index];
                    product += x * y;
                    mfccNorm += x * x;
                    phonemeNorm += y * y;
                }
                double denominator = Math.Sqrt(mfccNorm) * Math.Sqrt(phonemeNorm);
                double similarity = denominator > 0 ? Math.Max(0, product / denominator) : 0;
                // The JS port deliberately retains uLipSync's float32 score underflow.
                return (float)Math.Pow(similarity, 100);
            }
            double distance = 0;
            for (int index = 0; index < mfccCount; index++)
            {
                double x = (mfcc[index] - means[index]) / standardDeviations[index];
                double y = (phoneme[index] - means[index]) / standardDeviations[index];
                double delta = x - y;
                distance += compareMethod == 0 ? Math.Abs(delta) : delta * delta;
            }
            distance = compareMethod == 0 ? distance / mfccCount : Math.Sqrt(distance / mfccCount);
            return Math.Pow(10, -distance);
        }

        private MelBand[] CreateMelBands(int channels)
        {
            var bands = new MelBand[channels];
            double maxFrequency = targetSampleRate / 2.0;
            double maxMel = 1127 * Math.Log(maxFrequency / 700 + 1);
            int nyquistBin = sampleCount / 2;
            double frequencyStep = maxFrequency / nyquistBin;
            double melStep = maxMel / (channels + 1);
            for (int channel = 0; channel < channels; channel++)
            {
                double begin = 700 * (Math.Exp(melStep * channel / 1127) - 1);
                double center = 700 * (Math.Exp(melStep * (channel + 1) / 1127) - 1);
                double end = 700 * (Math.Exp(melStep * (channel + 2) / 1127) - 1);
                int start = (int)Math.Ceiling(begin / frequencyStep) + 1;
                int centerIndex = (int)Math.Floor(center / frequencyStep + 0.5);
                int last = Math.Min(nyquistBin - 1, (int)Math.Floor(end / frequencyStep));
                var weights = new double[Math.Max(0, last - start + 1)];
                for (int index = start; index <= last; index++)
                {
                    double frequency = frequencyStep * index;
                    double weight = index < centerIndex
                        ? (frequency - begin) / Math.Max(center - begin, NumberEpsilon)
                        : (end - frequency) / Math.Max(end - center, NumberEpsilon);
                    weight /= Math.Max((end - begin) * 0.5, NumberEpsilon);
                    weights[index - start] = Math.Max(0, weight);
                }
                bands[channel] = new MelBand { Start = start, Weights = weights };
            }
            return bands;
        }

        private static void ValidateProfile(MfccProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.mfccNum <= 0 || profile.mfccDataCount <= 0 || profile.targetSampleRate <= 0 ||
                profile.sampleCount <= 0 || (profile.sampleCount & (profile.sampleCount - 1)) != 0 ||
                profile.melFilterBankChannels <= profile.mfccNum)
                throw new ArgumentException("The profile requires positive sizes, a power-of-two sampleCount and more Mel bands than MFCC coefficients.", nameof(profile));
            if (profile.compareMethod < 0 || profile.compareMethod > 2)
                throw new ArgumentException("The profile compareMethod must be 0, 1 or 2.", nameof(profile));
            if (profile.mfccs == null || profile.mfccs.Length == 0)
                throw new ArgumentException("The profile must contain at least one MFCC entry.", nameof(profile));
            foreach (var entry in profile.mfccs)
            {
                if (entry == null || string.IsNullOrEmpty(entry.name))
                    throw new ArgumentException("Each MFCC entry requires a name.", nameof(profile));
                if (entry.mfccCalibrationDataList == null) continue;
                foreach (var vector in entry.mfccCalibrationDataList)
                {
                    if (vector == null || vector.array == null || vector.array.Length < profile.mfccNum)
                        throw new ArgumentException("Each calibration vector must contain at least mfccNum coefficients.", nameof(profile));
                    for (int index = 0; index < profile.mfccNum; index++)
                        if (!IsFinite(vector.array[index]))
                            throw new ArgumentException("Calibration coefficients must be finite.", nameof(profile));
                }
            }
        }

        private static Viseme ParseViseme(string name)
        {
            switch (name.ToUpperInvariant())
            {
                case "A": return Viseme.A;
                case "I": return Viseme.I;
                case "U": return Viseme.U;
                case "E": return Viseme.E;
                case "O": return Viseme.O;
                default: return Viseme.None;
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
        private static void RequireFinite(double value, string name)
        {
            if (!IsFinite(value)) throw new ArgumentOutOfRangeException(name, "The value must be finite.");
        }

        private sealed class MelBand
        {
            public int Start;
            public double[] Weights;
        }

        private sealed class RateBuffers
        {
            public readonly double[] Input;
            public readonly double[] Filtered;
            public readonly double[] Filter;
            public readonly int[] SourceIndices;

            public RateBuffers(int sourceRate, int targetRate, int targetCount)
            {
                int inputCount = checked((int)Math.Ceiling((double)targetCount * sourceRate / targetRate));
                Input = new double[inputCount];
                Filtered = new double[inputCount];
                double cutoff = (targetRate / 2.0 - 500) / sourceRate;
                double range = 500.0 / sourceRate;
                int length = checked((int)Math.Floor(3.1 / range + 0.5));
                // The source's even tap count keeps x away from zero in the sinc formula.
                if ((length + 1) % 2 == 0) length++;
                Filter = new double[length];
                for (int index = 0; index < length; index++)
                {
                    double x = index - (length - 1) / 2.0;
                    double angle = 2 * Math.PI * cutoff * x;
                    Filter[index] = angle == 0 ? 2 * cutoff : 2 * cutoff * Math.Sin(angle) / angle;
                }
                SourceIndices = new int[targetCount];
                double ratio = (double)sourceRate / targetRate;
                for (int index = 0; index < targetCount; index++)
                    SourceIndices[index] = Math.Min(inputCount - 1, (int)Math.Floor(index * ratio));
            }
        }
    }
}

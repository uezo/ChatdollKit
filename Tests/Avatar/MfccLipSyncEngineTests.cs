using ChatdollKit.Avatar.LipSync;
using System;
using System.Collections;
using ChatdollKit.Avatar;
using NUnit.Framework;

namespace ChatdollKit.Tests.Avatar
{
    public class MfccLipSyncEngineTests
    {
        private static readonly Viseme[] Vowels = { Viseme.A, Viseme.I, Viseme.U, Viseme.E, Viseme.O };

        private static IEnumerable JavaScriptFrames()
        {
            foreach (var fixture in MfccLipSyncJsFixtures.Cases)
                yield return new TestCaseData((object)fixture).SetName("MatchesJavaScript_" + fixture.Name);
        }

        [TestCaseSource(nameof(JavaScriptFrames))]
        public void MatchesJavaScriptMfccAndVisemeOutputs(object fixtureObject)
        {
            var fixture = (MfccLipSyncJsFixtures.Golden)fixtureObject;
            var profile = LoadProfile();
            profile.compareMethod = fixture.CompareMethod;
            profile.useStandardization = fixture.UseStandardization;
            var engine = new MfccLipSyncEngine(profile);
            var samples = CreateSamples(fixture.SampleRate);
            var original = (float[])samples.Clone();
            var mfcc = new double[profile.mfccNum];

            engine.ExtractMfcc(samples, 1, fixture.SampleRate, fixture.SamplePosition,
                mfcc, fixture.TimeOffsetSeconds);
            var result = engine.Process(samples, 1, fixture.SampleRate, fixture.SamplePosition,
                timeOffsetSeconds: fixture.TimeOffsetSeconds);

            // FFT/libm round-off varies slightly between .NET, Mono, and JavaScript runtimes.
            for (var i = 0; i < mfcc.Length; i++)
                Assert.That(mfcc[i], Is.EqualTo(fixture.Mfcc[i]).Within(1e-8), "MFCC coefficient " + i);
            Assert.That(result.RawVolume, Is.EqualTo(fixture.RawVolume).Within(1e-12));
            Assert.That(result.Volume, Is.EqualTo(fixture.Volume).Within(1e-10));
            for (var i = 0; i < Vowels.Length; i++)
                Assert.That(result.GetWeight(Vowels[i]), Is.EqualTo(fixture.Weights[i]).Within(1e-7), Vowels[i].ToString());
            Assert.That(result.MainViseme, Is.EqualTo(fixture.MainViseme));
            Assert.That(result.MainVisemeWeight, Is.EqualTo(fixture.MainVisemeWeight).Within(1e-10));
            CollectionAssert.AreEqual(original, samples, "Analysis must not modify playback PCM.");
        }

        [TestCase(16000)]
        [TestCase(24000)]
        [TestCase(44100)]
        [TestCase(48000)]
        public void StereoUsesFramePositionAndAveragesChannels(int sampleRate)
        {
            var mono = CreateSamples(sampleRate);
            var stereo = new float[mono.Length * 2];
            for (var frame = 0; frame < mono.Length; frame++)
            {
                // Averaging two different channels must recover the mono reference exactly.
                stereo[frame * 2] = mono[frame] * 2;
                stereo[frame * 2 + 1] = 0;
            }
            var engine = new MfccLipSyncEngine(LoadProfile());
            var samplePosition = sampleRate / 8;
            var expected = engine.Process(mono, 1, sampleRate, samplePosition);
            var actual = engine.Process(stereo, 2, sampleRate, samplePosition);
            AssertEquivalent(actual, expected);
        }

        [Test]
        public void GainChangesOnlyVolumeAndSilencesMouthAtZero()
        {
            var engine = new MfccLipSyncEngine(LoadProfile());
            var samples = CreateSamples(16000);
            var reference = engine.Process(samples, 1, 16000, 2880);
            var quieter = engine.Process(samples, 1, 16000, 2880, gain: 0.5);

            Assert.That(quieter.RawVolume, Is.EqualTo(reference.RawVolume));
            Assert.That(quieter.Volume, Is.EqualTo(reference.Volume + Math.Log10(0.5)).Within(1e-12));
            Assert.That(quieter.MainViseme, Is.EqualTo(reference.MainViseme));
            foreach (var vowel in Vowels)
                Assert.That(quieter.GetWeight(vowel),
                    Is.EqualTo(reference.GetWeight(vowel) * quieter.Volume / reference.Volume).Within(1e-12));

            engine.VolumeGain = 0.5;
            AssertEquivalent(engine.Process(samples, 1, 16000, 2880), quieter);
            AssertMouthClosed(engine.Process(samples, 1, 16000, 2880, gain: 0));
        }

        [Test]
        public void VolumeThresholdsAreConfigurable()
        {
            var engine = new MfccLipSyncEngine(LoadProfile());
            var samples = CreateSamples(16000);
            var reference = engine.Process(samples, 1, 16000, 2880);
            engine.MinVolume = Math.Log10(reference.RawVolume) - 1;
            engine.MaxVolume = Math.Log10(reference.RawVolume) + 1;
            Assert.That(engine.Process(samples, 1, 16000, 2880).Volume, Is.EqualTo(0.5).Within(1e-12));
            engine.MinVolume = Math.Log10(reference.RawVolume) + 1;
            engine.MaxVolume = engine.MinVolume + 1;
            AssertMouthClosed(engine.Process(samples, 1, 16000, 2880));
        }

        [Test]
        public void SilenceClearsPreviousAnalysisAndReusableMfccDestination()
        {
            var engine = new MfccLipSyncEngine(LoadProfile());
            var samples = CreateSamples(16000);
            var mfcc = new double[12];
            engine.ExtractMfcc(samples, 1, 16000, 2880, mfcc);
            Assert.That(mfcc, Has.Some.Not.EqualTo(0));

            var silence = new float[2048];
            engine.ExtractMfcc(silence, 1, 16000, 1500, mfcc);
            Assert.That(mfcc, Is.All.EqualTo(0));
            var result = engine.Process(silence, 1, 16000, 1500);
            AssertMouthClosed(result);
            Assert.That(result.RawVolume, Is.Zero);
            Assert.That(result.MainViseme, Is.EqualTo(Viseme.None));
            AssertMouthClosed(engine.Process(Array.Empty<float>(), 1, 16000, 0));
        }

        [Test]
        public void PositionAndOffsetClampToAvailableFrames()
        {
            var engine = new MfccLipSyncEngine(LoadProfile());
            var samples = CreateSamples(24000);
            AssertMouthClosed(engine.Process(samples, 1, 24000, 0));
            AssertMouthClosed(engine.Process(samples, 1, 24000, -100));
            AssertMouthClosed(engine.Process(samples, 1, 24000, 100, timeOffsetSeconds: -1));
            var end = engine.Process(samples, 1, 24000, samples.Length);
            AssertEquivalent(engine.Process(samples, 1, 24000, samples.Length + 100), end);
            AssertEquivalent(engine.Process(samples, 1, 24000, 100, timeOffsetSeconds: 1), end);
        }

        [Test]
        public void RateChangesReuseEngineWithoutKeepingOldFrameData()
        {
            var shared = new MfccLipSyncEngine(LoadProfile());
            foreach (var sampleRate in new[] { 48000, 16000, 44100, 24000, 48000 })
            {
                var samples = CreateSamples(sampleRate);
                var expected = new MfccLipSyncEngine(LoadProfile()).Process(samples, 1, sampleRate, sampleRate / 10);
                AssertEquivalent(shared.Process(samples, 1, sampleRate, sampleRate / 10), expected);
            }
        }

#if !UNITY_5_3_OR_NEWER
        [Test]
        public void WarmedAnalysisDoesNotAllocatePerFrame()
        {
            var engine = new MfccLipSyncEngine(LoadProfile());
            var samples = CreateSamples(48000);
            for (var i = 0; i < 10; i++) engine.Process(samples, 1, 48000, 8000 + i);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var totalVolume = 0.0;
            for (var i = 0; i < 100; i++)
                totalVolume += engine.Process(samples, 1, 48000, 8000 + i).Volume;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(totalVolume, Is.GreaterThan(0));
            Assert.That(allocated, Is.Zero, "The same-rate playback analysis path must reuse its working buffers.");
        }
#endif

        [Test]
        public void NonFinitePcmIsTreatedAsSilence()
        {
            var samples = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f };
            var result = new MfccLipSyncEngine(LoadProfile()).Process(samples, 1, 16000, samples.Length);
            AssertMouthClosed(result);
            Assert.That(result.RawVolume, Is.Zero);
        }

        [Test]
        public void AllScoresUnderflowDoesNotSelectAnArbitraryVowel()
        {
            var profile = LoadProfile(@"{""mfccNum"":2,""mfccDataCount"":1,
                ""melFilterBankChannels"":4,""targetSampleRate"":16000,""sampleCount"":1024,
                ""compareMethod"":1,""mfccs"":[
                {""name"":""A"",""mfccCalibrationDataList"":[{""array"":[10000,10000]}]},
                {""name"":""I"",""mfccCalibrationDataList"":[{""array"":[20000,20000]}]}]}");
            var result = new MfccLipSyncEngine(profile).Process(CreateSamples(16000), 1, 16000, 2880);
            Assert.That(result.RawVolume, Is.GreaterThan(0));
            AssertMouthClosed(result);
            Assert.That(result.MainViseme, Is.EqualTo(Viseme.None));
        }

        [Test]
        public void CosineScoresPreserveFloat32UnderflowBehavior()
        {
            // The reference JS scores all underflow to float32 zero for this low-noise signal.
            // Keeping the scores as double would produce a spurious vowel instead.
            var result = new MfccLipSyncEngine(LoadProfile())
                .Process(CreateSamples(16000, noiseScale: 8), 1, 16000, 2880);
            Assert.That(result.RawVolume, Is.GreaterThan(0));
            AssertMouthClosed(result);
            Assert.That(result.MainViseme, Is.EqualTo(Viseme.None));
        }

        [Test]
        public void RejectsInvalidProfileDimensionsAndCalibration()
        {
            Assert.Throws<ArgumentNullException>(() => new MfccLipSyncEngine(null));
            var profile = LoadProfile();
            profile.sampleCount = 1000;
            Assert.Catch<ArgumentException>(() => new MfccLipSyncEngine(profile));
            profile = LoadProfile();
            profile.mfccNum = profile.melFilterBankChannels;
            Assert.Catch<ArgumentException>(() => new MfccLipSyncEngine(profile));
            profile = LoadProfile();
            profile.targetSampleRate = 0;
            Assert.Catch<ArgumentException>(() => new MfccLipSyncEngine(profile));
            profile = LoadProfile();
            profile.mfccs = null;
            Assert.Catch<ArgumentException>(() => new MfccLipSyncEngine(profile));
            profile = LoadProfile();
            profile.mfccs[0].mfccCalibrationDataList[0].array = new double[1];
            Assert.Catch<ArgumentException>(() => new MfccLipSyncEngine(profile));
            profile = LoadProfile();
            profile.mfccs[0].mfccCalibrationDataList[0].array[0] = double.NaN;
            Assert.Catch<ArgumentException>(() => new MfccLipSyncEngine(profile));
        }

        [Test]
        public void RejectsUnsupportedRatesAndMalformedInterleavedAudio()
        {
            var engine = new MfccLipSyncEngine(LoadProfile());
            Assert.Catch<ArgumentException>(() => engine.Process(new float[1024], 1, 8000, 1000));
            Assert.Catch<ArgumentException>(() => engine.Process(new float[1024], 1, 0, 1000));
            Assert.Catch<ArgumentException>(() => engine.Process(new float[1024], 0, 16000, 1000));
            Assert.Catch<ArgumentException>(() => engine.Process(new float[3], 2, 16000, 1));
            Assert.Throws<ArgumentNullException>(() => engine.Process(null, 1, 16000, 0));
            Assert.Catch<ArgumentException>(() => engine.ExtractMfcc(new float[1024], 1, 16000, 1000, new double[11]));
        }

        private static void AssertEquivalent(LipSyncResult actual, LipSyncResult expected)
        {
            Assert.That(actual.RawVolume, Is.EqualTo(expected.RawVolume).Within(1e-12));
            Assert.That(actual.Volume, Is.EqualTo(expected.Volume).Within(1e-12));
            Assert.That(actual.MainViseme, Is.EqualTo(expected.MainViseme));
            Assert.That(actual.MainVisemeWeight, Is.EqualTo(expected.MainVisemeWeight).Within(1e-12));
            foreach (var vowel in Vowels)
                Assert.That(actual.GetWeight(vowel), Is.EqualTo(expected.GetWeight(vowel)).Within(1e-12));
        }

        private static void AssertMouthClosed(LipSyncResult result)
        {
            Assert.That(result.Volume, Is.Zero);
            Assert.That(result.MainVisemeWeight, Is.Zero);
            foreach (var vowel in Vowels) Assert.That(result.GetWeight(vowel), Is.Zero);
            Assert.That(result.GetWeight(Viseme.None), Is.Zero);
        }

        private static MfccProfile LoadProfile(string json = MfccLipSyncJsFixtures.ProfileJson)
        {
#if UNITY_5_3_OR_NEWER
            return UnityEngine.JsonUtility.FromJson<MfccProfile>(json);
#else
            return Newtonsoft.Json.JsonConvert.DeserializeObject<MfccProfile>(json);
#endif
        }

        private static float[] CreateSamples(int sampleRate, int noiseScale = 512)
        {
            var samples = new float[sampleRate / 4];
            uint state = 0x12345678;
            for (var i = 0; i < samples.Length; i++)
            {
                state = unchecked(state * 1664525 + 1013904223);
                var noise = (int)(state >> 24) - 128;
                samples[i] = (Triangle(i, 137, sampleRate) * 3
                    + Triangle(i, 719, sampleRate) * 2
                    + Triangle(i, 1223, sampleRate)
                    + Triangle(i, 2549, sampleRate) + noise * noiseScale) / 4194304f;
            }
            return samples;
        }

        private static int Triangle(int frame, int frequency, int sampleRate)
        {
            var phase = (int)((long)frame * frequency * 65536 / sampleRate % 65536);
            return phase < 32768 ? phase - 16384 : 49152 - phase;
        }
    }
}

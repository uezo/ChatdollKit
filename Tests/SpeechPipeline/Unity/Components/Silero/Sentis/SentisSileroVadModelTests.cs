using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.SpeechPipeline.VAD.Silero.Sentis;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Silero.Sentis
{
    public sealed class SentisSileroVadModelTests
    {
        // Same original model and deterministic PCM sequence as
        // OnnxSileroVadModelTests: Silero 6.0.0 OnnxWrapper, CPU ORT 1.21.1.
        private static readonly float[] ReferenceProbabilities =
        {
            0.01201203465f, 0.05746787786f, 0.05079486966f, 0.04458412528f,
            0.03277337551f, 0.01497119665f, 0.01267051697f, 0.0168544054f,
            0.02390763164f, 0.01497945189f, 0.004746317863f, 0.006074130535f
        };

        private byte[] modelBytes;

        [OneTimeSetUp]
        public void LoadBundledModel()
        {
            var asset = Resources.Load<TextAsset>("ChatdollKit/Silero/silero_vad_16k.sentis");
            Assert.That(asset, Is.Not.Null, "The bundled serialized 16 kHz model must be available in Resources.");
            modelBytes = asset.bytes;
            Assert.That(modelBytes.Length, Is.GreaterThan(0));
        }

        [Test]
        public void BundledModelMatchesTheOriginalOnnxRuntimeProbabilitySequence()
        {
            using (var model = new SentisSileroVadModel(modelBytes))
            {
                var frames = CreateFrames(ReferenceProbabilities.Length);
                for (var i = 0; i < frames.Length; i++)
                    Assert.That(Predict(model, frames[i]), Is.EqualTo(ReferenceProbabilities[i]).Within(0.00001f), $"Frame {i}");
            }
        }

        [Test]
        public void ResetRestoresStateAndContext()
        {
            using (var model = new SentisSileroVadModel(modelBytes))
            {
                var frames = CreateFrames(8);
                var first = new float[frames.Length];
                for (var i = 0; i < frames.Length; i++) first[i] = Predict(model, frames[i]);
                model.ResetStates();
                for (var i = 0; i < frames.Length; i++)
                    Assert.That(Predict(model, frames[i]), Is.EqualTo(first[i]).Within(0.000001f));
            }
        }

        [Test]
        public void InvalidAndCanceledFramesDoNotAdvanceAnExistingStream()
        {
            using (var model = new SentisSileroVadModel(modelBytes))
            {
                var frames = CreateFrames(4);
                Predict(model, frames[0]);
                Assert.Throws<ArgumentNullException>(() => Predict(model, null));
                Assert.Throws<ArgumentOutOfRangeException>(() => Predict(model, frames[1], 44100));
                Assert.Throws<ArgumentOutOfRangeException>(() => Predict(model, new float[256], 8000));
                Assert.Throws<ArgumentException>(() => Predict(model, new float[511]));
                var invalid = new float[512];
                invalid[300] = float.NaN;
                Assert.Throws<ArgumentException>(() => Predict(model, invalid));
                invalid[300] = float.PositiveInfinity;
                Assert.Throws<ArgumentException>(() => Predict(model, invalid));
                Assert.Throws<OperationCanceledException>(() => model.PredictAsync(frames[1], 16000,
                    new CancellationToken(true)).GetAwaiter().GetResult());

                for (var i = 1; i < frames.Length; i++)
                    Assert.That(Predict(model, frames[i]), Is.EqualTo(ReferenceProbabilities[i]).Within(0.00001f));
            }
        }

        [Test]
        public void InstancesKeepIndependentState()
        {
            using (var first = new SentisSileroVadModel(modelBytes))
            using (var other = new SentisSileroVadModel(modelBytes))
            {
                var frames = CreateFrames(6);
                for (var i = 0; i < frames.Length; i++)
                {
                    Assert.That(Predict(first, frames[i]), Is.EqualTo(ReferenceProbabilities[i]).Within(0.00001f));
                    other.ResetStates();
                    Predict(other, frames[i]);
                }
            }
        }

        [Test]
        public void BackgroundThreadCallsAreRejectedBeforeMutatingState()
        {
            using (var model = new SentisSileroVadModel(modelBytes))
            {
                var frames = CreateFrames(2);
                Predict(model, frames[0]);
                AssertOnBackgroundThread(() => new SentisSileroVadModel(modelBytes));
                AssertOnBackgroundThread(() => Predict(model, frames[1]));
                AssertOnBackgroundThread(() => model.ResetStates());
                AssertOnBackgroundThread(() => model.Dispose());
                Assert.That(Predict(model, frames[1]), Is.EqualTo(ReferenceProbabilities[1]).Within(0.00001f));
            }
        }

        [Test]
        public void DisposeIsIdempotentAndRejectsFurtherUse()
        {
            var model = new SentisSileroVadModel(modelBytes);
            model.Dispose();
            Assert.DoesNotThrow(() => model.Dispose());
            Assert.Throws<ObjectDisposedException>(() => Predict(model, new float[512]));
            Assert.Throws<ObjectDisposedException>(() => model.PredictAsync(new float[512], 16000,
                new CancellationToken(true)).GetAwaiter().GetResult());
            Assert.Throws<ObjectDisposedException>(() => model.ResetStates());
        }

        [Test]
        public void ConstructorRejectsMissingModelData()
        {
            Assert.Throws<ArgumentNullException>(() => new SentisSileroVadModel(null));
            Assert.Throws<ArgumentException>(() => new SentisSileroVadModel(Array.Empty<byte>()));
        }

        [TestCase("input")]
        [TestCase("state")]
        [TestCase("sr")]
        [TestCase("stateN")]
        public void ConstructorRejectsAnIncompatibleModelInterface(string difference)
        {
            // A tiny pass-through graph isolates interface validation from the
            // expensive network, and serializes through the real Sentis loader.
            var model = new Model
            {
                inputs = new List<Model.Input>
                {
                    new Model.Input { name = "input", index = 0, dataType = DataType.Float,
                        shape = new DynamicTensorShape(new TensorShape(1, difference == "input" ? 512 : 576)) },
                    new Model.Input { name = "state", index = 1, dataType = DataType.Float,
                        shape = new DynamicTensorShape(new TensorShape(2, 1, difference == "state" ? 64 : 128)) }
                },
                outputs = new List<Model.Output>
                {
                    new Model.Output { name = "output", index = 0 },
                    new Model.Output { name = difference == "stateN" ? "unexpected" : "stateN", index = 1 }
                }
            };
            if (difference == "sr")
                model.inputs.Add(new Model.Input { name = "sr", index = 2, dataType = DataType.Int,
                    shape = new DynamicTensorShape(new TensorShape(1)) });

            using (var stream = new MemoryStream())
            {
                ModelWriter.Save(stream, model);
                Assert.Throws<ArgumentException>(() => new SentisSileroVadModel(stream.ToArray()));
            }
        }

        private static float Predict(SentisSileroVadModel model, float[] samples, int sampleRate = 16000)
        {
            var prediction = model.PredictAsync(samples, sampleRate);
            Assert.That(prediction.Status, Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded),
                "CPU inference must complete before returning so reset cannot race with pending work.");
            return prediction.GetAwaiter().GetResult();
        }

        private static void AssertOnBackgroundThread(Action action)
        {
            var exception = Task.Run(() =>
            {
                try { action(); return null; }
                catch (Exception error) { return error; }
            }).GetAwaiter().GetResult();
            Assert.That(exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.Message, Does.Contain("main thread"));
        }

        private static float[][] CreateFrames(int count)
        {
            var frames = new float[count][];
            uint random = 123456789;
            for (var frame = 0; frame < count; frame++)
            {
                var samples = frames[frame] = new float[512];
                for (var i = 0; i < samples.Length; i++)
                {
                    random = unchecked(1664525 * random + 1013904223);
                    var pcm = (int)(random >> 16) - 32768;
                    samples[i] = frame % 4 == 0 ? 0f : pcm / 32768f;
                }
            }
            return frames;
        }
    }
}

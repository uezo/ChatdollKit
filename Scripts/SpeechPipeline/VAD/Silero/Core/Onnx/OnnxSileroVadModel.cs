using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ChatdollKit.SpeechPipeline.VAD.Silero.Onnx
{
    /// <summary>
    /// Stateful CPU inference for the Silero ONNX input/state/sr model interface.
    /// Use a separate instance for each independent audio stream.
    /// </summary>
    public sealed class OnnxSileroVadModel : ISileroVadModel
    {
        private readonly object sync = new object();
        private readonly InferenceSession session;
        private readonly float[] state = new float[2 * 128];
        private float[] context = Array.Empty<float>();
        private int lastSampleRate;
        private bool disposed;

        /// <summary>
        /// Creates a CPU session. Loading model bytes is the caller's responsibility,
        /// so Unity StreamingAssets can be read asynchronously on every platform.
        /// </summary>
        public OnnxSileroVadModel(byte[] modelBytes, int intraOpNumThreads = 1, int interOpNumThreads = 1)
        {
            if (modelBytes == null) throw new ArgumentNullException(nameof(modelBytes));
            if (modelBytes.Length == 0) throw new ArgumentException("Model bytes must not be empty.", nameof(modelBytes));
            if (intraOpNumThreads < 1) throw new ArgumentOutOfRangeException(nameof(intraOpNumThreads));
            if (interOpNumThreads < 1) throw new ArgumentOutOfRangeException(nameof(interOpNumThreads));

            using (var options = new SessionOptions())
            {
                options.IntraOpNumThreads = intraOpNumThreads;
                options.InterOpNumThreads = interOpNumThreads;
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                // CPU is ONNX Runtime's default execution provider.
                var createdSession = new InferenceSession(modelBytes, options);
                try
                {
                    RequireTensor(createdSession.InputMetadata, "input", typeof(float), 2);
                    RequireTensor(createdSession.InputMetadata, "state", typeof(float), 3);
                    RequireTensor(createdSession.InputMetadata, "sr", typeof(long), 0);
                    RequireTensor(createdSession.OutputMetadata, "output", typeof(float), 2);
                    RequireTensor(createdSession.OutputMetadata, "stateN", typeof(float), 3);
                    session = createdSession;
                }
                catch
                {
                    createdSession.Dispose();
                    throw;
                }
            }
        }

        public OnnxSileroVadModel(string modelPath, int intraOpNumThreads = 1, int interOpNumThreads = 1)
            : this(File.ReadAllBytes(modelPath), intraOpNumThreads, interOpNumThreads)
        {
        }

        public UniTask<float> PredictAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(Predict(samples, sampleRate));
        }

        /// <summary>
        /// Processes exactly 512 samples at 16 kHz or 256 samples at 8 kHz.
        /// The model's recurrent state and 64/32 samples of context are retained.
        /// Changing the sample rate resets both, matching Silero's Python wrapper.
        /// </summary>
        public float Predict(float[] samples, int sampleRate)
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (samples == null) throw new ArgumentNullException(nameof(samples));
                if (sampleRate != 16000 && sampleRate != 8000)
                {
                    throw new ArgumentOutOfRangeException(nameof(sampleRate), "Silero supports 8000 or 16000 Hz.");
                }

                var sampleCount = sampleRate == 16000 ? 512 : 256;
                if (samples.Length != sampleCount)
                {
                    throw new ArgumentException($"Expected {sampleCount} samples at {sampleRate} Hz.", nameof(samples));
                }

                if (lastSampleRate != sampleRate)
                {
                    ClearStates();
                    context = new float[sampleRate == 16000 ? 64 : 32];
                }

                var input = new float[context.Length + sampleCount];
                Array.Copy(context, input, context.Length);
                Array.Copy(samples, 0, input, context.Length, sampleCount);
                var inputs = new[]
                {
                    NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, new[] { 1, input.Length })),
                    NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(state, new[] { 2, 1, 128 })),
                    NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new[] { (long)sampleRate }, Array.Empty<int>()))
                };

                using (var results = session.Run(inputs))
                {
                    var output = results.First(value => value.Name == "output").AsTensor<float>();
                    var nextState = results.First(value => value.Name == "stateN").AsTensor<float>();
                    if (output.Length != 1 || nextState.Length != state.Length)
                    {
                        throw new InvalidOperationException("Silero returned an unexpected probability or state shape.");
                    }

                    nextState.ToArray().CopyTo(state, 0);
                    Array.Copy(samples, sampleCount - context.Length, context, 0, context.Length);
                    lastSampleRate = sampleRate;
                    return output.GetValue(0);
                }
            }
        }

        public void ResetStates()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                ClearStates();
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    session.Dispose();
                }
                finally
                {
                    ClearStates();
                }
            }
        }

        private void ClearStates()
        {
            Array.Clear(state, 0, state.Length);
            context = Array.Empty<float>();
            lastSampleRate = 0;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(OnnxSileroVadModel));
        }

        private static void RequireTensor(IReadOnlyDictionary<string, NodeMetadata> metadata, string name, Type type, int rank)
        {
            if (!metadata.TryGetValue(name, out var tensor) || !tensor.IsTensor ||
                tensor.ElementType != type || tensor.Dimensions.Length != rank)
            {
                throw new ArgumentException($"The model must expose the Silero '{name}' tensor with type {type.Name} and rank {rank}.");
            }
        }
    }
}

using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.InferenceEngine;

namespace ChatdollKit.SpeechPipeline.VAD.Silero.Sentis
{
    /// <summary>
    /// Stateful CPU inference for the fixed 16 kHz Silero model serialized by Sentis.
    /// Create, predict, reset, and dispose on Unity's main thread. Use a separate
    /// instance for each audio stream. No ONNX Runtime package or browser script is required.
    /// </summary>
    public sealed class SentisSileroVadModel : ISileroVadModel
    {
        private const int SampleRate = 16000;
        private const int FrameSamples = 512;
        private const int ContextSamples = 64;
        private static readonly TensorShape InputShape = new TensorShape(1, FrameSamples + ContextSamples);
        private static readonly TensorShape StateShape = new TensorShape(2, 1, 128);
        private static readonly TensorShape OutputShape = new TensorShape(1, 1);

        private readonly float[] input = new float[FrameSamples + ContextSamples];
        private readonly float[] state = new float[2 * 128];
        private readonly float[] context = new float[ContextSamples];
        private Worker worker;
        private Tensor<float> inputTensor;
        private Tensor<float> stateTensor;
        private bool disposed;

        /// <summary>
        /// Loads serialized .sentis bytes, not ONNX bytes. The caller is responsible
        /// for reading the asset, for example from Resources as a TextAsset.
        /// </summary>
        public SentisSileroVadModel(byte[] serializedModelBytes)
        {
            if (serializedModelBytes == null) throw new ArgumentNullException(nameof(serializedModelBytes));
            if (serializedModelBytes.Length == 0)
                throw new ArgumentException("Serialized Sentis model bytes must not be empty.", nameof(serializedModelBytes));
            RequireMainThread();

            Model model;
            using (var stream = new MemoryStream(serializedModelBytes, false))
                model = ModelLoader.Load(stream);
            ValidateModel(model);

            try
            {
                worker = new Worker(model, BackendType.CPU);
                inputTensor = new Tensor<float>(InputShape);
                stateTensor = new Tensor<float>(StateShape);
            }
            catch
            {
                ReleaseResources();
                throw;
            }
        }

        /// <summary>
        /// Processes exactly 512 mono samples at 16 kHz and retains recurrent state
        /// plus 64 samples of context. CPU work completes before this method returns;
        /// reset and disposal therefore cannot race with a pending prediction.
        /// </summary>
        public UniTask<float> PredictAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            RequireMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (sampleRate != SampleRate)
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "The Sentis Silero model supports only 16000 Hz.");
            if (samples.Length != FrameSamples)
                throw new ArgumentException($"Expected {FrameSamples} samples at {SampleRate} Hz.", nameof(samples));
            for (var i = 0; i < samples.Length; i++)
            {
                if (float.IsNaN(samples[i]) || float.IsInfinity(samples[i]))
                    throw new ArgumentException("Audio samples must be finite.", nameof(samples));
            }

            Array.Copy(context, input, ContextSamples);
            Array.Copy(samples, 0, input, ContextSamples, FrameSamples);
            inputTensor.Upload(input);
            stateTensor.Upload(state);
            worker.SetInput("input", inputTensor);
            worker.SetInput("state", stateTensor);
            worker.Schedule();

            // PeekOutput tensors belong to the worker. Only the two input tensors
            // are owned by this adapter, and must be disposed separately.
            var output = worker.PeekOutput("output") as Tensor<float>;
            var nextState = worker.PeekOutput("stateN") as Tensor<float>;
            if (output == null || output.shape != OutputShape || nextState == null || nextState.shape != StateShape)
                throw new InvalidOperationException("Silero returned an unexpected probability or state shape.");

            output.CompleteAllPendingOperations();
            nextState.CompleteAllPendingOperations();
            var probability = output[0];
            var nextStateValues = nextState.AsReadOnlySpan();
            if (float.IsNaN(probability) || probability < 0f || probability > 1f)
                throw new InvalidOperationException("Silero returned an invalid speech probability.");
            for (var i = 0; i < nextStateValues.Length; i++)
            {
                if (float.IsNaN(nextStateValues[i]) || float.IsInfinity(nextStateValues[i]))
                    throw new InvalidOperationException("Silero returned an invalid recurrent state.");
            }

            // Cancellation may arrive from another thread while CPU jobs complete.
            // Commit neither state nor context if that prediction was canceled.
            cancellationToken.ThrowIfCancellationRequested();
            nextStateValues.CopyTo(state);
            Array.Copy(input, input.Length - ContextSamples, context, 0, ContextSamples);
            return UniTask.FromResult(probability);
        }

        public void ResetStates()
        {
            ThrowIfDisposed();
            RequireMainThread();
            ClearStates();
        }

        public void Dispose()
        {
            if (disposed) return;
            RequireMainThread();
            disposed = true;
            try
            {
                ReleaseResources();
            }
            finally
            {
                ClearStates();
            }
        }

        private void ReleaseResources()
        {
            try
            {
                worker?.Dispose();
            }
            finally
            {
                worker = null;
                try
                {
                    inputTensor?.Dispose();
                }
                finally
                {
                    inputTensor = null;
                    stateTensor?.Dispose();
                    stateTensor = null;
                }
            }
        }

        private void ClearStates()
        {
            Array.Clear(state, 0, state.Length);
            Array.Clear(context, 0, context.Length);
            Array.Clear(input, 0, input.Length);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SentisSileroVadModel));
        }

        private static void RequireMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
                throw new InvalidOperationException("Sentis Silero must be used on Unity's main thread.");
        }

        private static void ValidateModel(Model model)
        {
            if (model.inputs.Count != 2)
                throw new ArgumentException("Use the fixed 16 kHz Silero model with only 'input' and 'state' inputs.");
            RequireInput(model, "input", InputShape);
            RequireInput(model, "state", StateShape);
            if (!model.outputs.Exists(output => output.name == "output") ||
                !model.outputs.Exists(output => output.name == "stateN"))
                throw new ArgumentException("The Silero model must expose 'output' and 'stateN' outputs.");
        }

        private static void RequireInput(Model model, string name, TensorShape expectedShape)
        {
            var matchingInputs = model.inputs.FindAll(input => input.name == name);
            if (matchingInputs.Count != 1 || matchingInputs[0].dataType != DataType.Float ||
                !matchingInputs[0].shape.IsStatic() || matchingInputs[0].shape.ToTensorShape() != expectedShape)
                throw new ArgumentException($"The model must expose the Silero '{name}' float tensor with fixed shape {expectedShape}.");
        }
    }
}

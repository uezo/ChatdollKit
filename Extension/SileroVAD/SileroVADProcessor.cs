using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine.Networking;
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
# endif

namespace ChatdollKit.Extension.SileroVAD
{
    public class SileroVADProcessor : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "IsVoiceDetected")]
        private static extern int IsVoiceDetectedJS();

        [DllImport("__Internal")]
        private static extern float GetVoiceProbability();

        [DllImport("__Internal")]
        private static extern void SetVADThreshold(float threshold);

        private float lastThreshold;
        public bool IsMuted { get; private set; } = false;
#else
        [Header("VAD Model Settings")]
        [Tooltip("The filename of the ONNX model in the StreamingAssets folder.")]
        [SerializeField]
        private string onnxModelName = "silero_vad.onnx";

        // The chunk size for SireloVAD is 512.
        private int sampleSize = 512;

        // The sampling rate for SileroVAD is 16000.
        private long modelSamplingRate = 16000;
        private InferenceSession session;
        private float[] state = new float[256];
        private readonly List<float> audioBuffer = new List<float>();
#endif
        [Tooltip("Confidence threshold for detecting speech (0.0 to 1.0).")]
        [SerializeField]
        private float threshold = 0.5f;

        [Tooltip("The speech probability from the most recent inference.")]
        [SerializeField]
        private float lastProbability = 0f;

        public bool IsVoiceDetected { get { return lastProbability > threshold; } }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void Start()
        {
            lastThreshold = threshold;
            SetVADThreshold(threshold);
        }
#endif
        public void Initialize()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Do nothing on WebGL runtime
#else
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                string modelPath = Path.Combine(Application.persistentDataPath, onnxModelName);
                using (UnityWebRequest www = UnityWebRequest.Get(Path.Combine(Application.streamingAssetsPath, onnxModelName)))
                {
                    www.SendWebRequest();
                    while (!www.isDone) { }
                    
                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        File.WriteAllBytes(modelPath, www.downloadHandler.data);
                        Debug.Log($"Successfully copied {onnxModelName} to persistent storage");
                    }
                    else
                    {
                        throw new Exception($"Failed to load {onnxModelName} from StreamingAssets: {www.error}");
                    }
                }
#else
                string modelPath = Path.Combine(Application.streamingAssetsPath, onnxModelName);
#endif
                session = new InferenceSession(modelPath, new SessionOptions());
                ResetStates();
                Debug.Log($"VAD Initialized. Expecting {modelSamplingRate}Hz audio. Processing chunk size: {sampleSize}.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"VAD initialization failed: {ex.Message}\n{ex.StackTrace}");
            }
#endif
        }

        public bool IsVoiced(float[] newSamples, float _)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (IsMuted) return false;

            if (Mathf.Abs(threshold - lastThreshold) > 0.01f)
            {
                SetVADThreshold(threshold);
                lastThreshold = threshold;
            }

            lastProbability = GetVoiceProbability();

            return IsVoiceDetectedJS() == 1;
#else
            if (session == null || newSamples == null) return false;

            audioBuffer.AddRange(newSamples);

            if (audioBuffer.Count < sampleSize)
            {
                return false;
            }

            while (audioBuffer.Count >= sampleSize)
            {
                var chunkToProcess = new float[sampleSize];
                audioBuffer.CopyTo(0, chunkToProcess, 0, sampleSize);
                audioBuffer.RemoveRange(0, sampleSize);

                if (RunInference(chunkToProcess))
                {
                    audioBuffer.Clear();
                    ResetStates();
                    return true;
                }
            }

            return false;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        public void Mute(bool mute)
        {
            IsMuted = mute;
        }
#else
        private bool RunInference(float[] audioSamples)
        {
            try
            {
                var inputShape = new int[] { 1, sampleSize };
                var srShape = new int[] { 1 };
                var stateShape = new int[] { 2, 1, 128 };

                var inputTensor = new DenseTensor<float>(audioSamples, inputShape);
                var srTensor = new DenseTensor<long>(new long[] { this.modelSamplingRate }, srShape);
                var stateTensor = new DenseTensor<float>(this.state, stateShape);

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input", inputTensor),
                    NamedOnnxValue.CreateFromTensor("sr", srTensor),
                    NamedOnnxValue.CreateFromTensor("state", stateTensor)
                };

                using (var results = session.Run(inputs))
                {
                    var outputValue = results.FirstOrDefault(v => v.Name == "output");
                    var stateNValue = results.FirstOrDefault(v => v.Name == "stateN");

                    if (outputValue != null)
                    {
                        lastProbability = outputValue.AsTensor<float>().ToArray()[0];
                        if (stateNValue != null)
                        {
                            stateNValue.AsTensor<float>().ToArray().CopyTo(this.state, 0);
                        }
                        return lastProbability > threshold;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"VAD processing error: {ex.Message}\n{ex.StackTrace}");
            }
            return false;
        }

        public void ResetStates()
        {
            System.Array.Clear(this.state, 0, this.state.Length);
        }

        void OnDestroy()
        {
            session?.Dispose();
        }
#endif
    }
}

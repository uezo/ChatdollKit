using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
#endif

namespace ChatdollKit.SpeechListener
{
#if !UNITY_WEBGL || UNITY_EDITOR
    public class UnityMicrophoneProvider : IMicrophoneProvider
    {
        public bool IsRecording(string deviceName) => Microphone.IsRecording(deviceName);
        public AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency) 
            => Microphone.Start(deviceName, loop, lengthSec, frequency);
        public void End(string deviceName) => Microphone.End(deviceName);
        public int GetPosition(string deviceName) => Microphone.GetPosition(deviceName);
        public string[] devices => Microphone.devices;
    }
#endif

    public class MicrophoneManager : MonoBehaviour, IMicrophoneManager
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void InitWebGLMicrophone(string targetObjectName, bool useMalloc);
        [DllImport("__Internal")]
        private static extern void StartWebGLMicrophone();
        [DllImport("__Internal")]
        private static extern void EndWebGLMicrophone();
        [DllImport("__Internal")]
        private static extern int IsWebGLMicrophoneRecording();
        [DllImport("__Internal")]
        private static extern void JsFree(IntPtr ptr);
        private Queue<float[]> webGLSamplesBuffer = new Queue<float[]>();
#endif
        public string MicrophoneDevice;
        public int SampleRate = 44100;
        public float NoiseGateThresholdDb = -50.0f;
        public bool AutoStart = true;
        public bool IsDebug = false;
        public IMicrophoneProvider MicrophoneProvider { get; set; }
        [SerializeField]
        private bool useMallocInWebGL = true;

        // Expose on inspector for debugging
        public bool IsRecording;
        public float CurrentVolumeDb;
        public int CurrentSamples;

        public bool IsMuted { get; private set; } = false;
        private AudioClip microphoneClip;
        private int lastSamplePosition;
        private float linearNoiseGateThreshold;
        private List<RecordingSession> activeSessions = new List<RecordingSession>();

#if UNITY_WEBGL && !UNITY_EDITOR
        private byte[] tempBytes;
#endif

        private void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SampleRate = 44100;
            InitWebGLMicrophone(gameObject.name, useMallocInWebGL);
            if (!useMallocInWebGL)
            {
                Debug.LogWarning("Set `useMallocInWebGL = true` to improve performance.");
            }
#endif
            UpdateLinearVolumes();
            if (AutoStart)
            {
                StartMicrophone();
            }
        }

        private void Update()
        {
            var samples = GetAmplitudeData();
            CurrentVolumeDb = GetCurrentMicVolumeDb(samples);

            // NOTE: ProcessSamples may trigger OnRecordingComplete callback which calls StopRecording,
            // modifying activeSessions during iteration. Reverse loop prevents Collection modified exception. 
            for (int i = activeSessions.Count - 1; i >= 0; i--)
            {
                activeSessions[i].ProcessSamples(samples, linearNoiseGateThreshold);
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            IsRecording = IsWebGLMicrophoneRecording() == 1;
#else
            IsRecording = MicrophoneProvider.IsRecording(MicrophoneDevice);
#endif
        }

        // Control microphone device
#if UNITY_WEBGL && !UNITY_EDITOR
        public void StartMicrophone()
        {
            if (IsWebGLMicrophoneRecording() == 1)
            {
                Debug.Log("WebGLMicrophone already started");
                return;
            }

            StartWebGLMicrophone();

            if (IsDebug) Debug.Log("WebGLMicrophone started");
        }

        public void StopMicrophone()
        {
            if (IsWebGLMicrophoneRecording() == 1)
            {
                EndWebGLMicrophone();
            }

            if (IsDebug) Debug.Log("WebGLMicrophone stopped");
        }
#else
        public void StartMicrophone()
        {
            if (MicrophoneProvider == null)
            {
                Debug.Log($"Use Unity built-in Microphone");
                MicrophoneProvider = new UnityMicrophoneProvider();
            }

            if (microphoneClip != null)
                {
                    Debug.Log("Microphone already started");
                    return;
                }

            if (MicrophoneDevice == null)
            {
                MicrophoneDevice = MicrophoneProvider.devices[0];
            }

            microphoneClip = MicrophoneProvider.Start(MicrophoneDevice, true, 1, SampleRate);
            lastSamplePosition = 0;

            if (IsDebug) Debug.Log($"Microphone started: {MicrophoneDevice}");
        }

        public void StopMicrophone()
        {
            MicrophoneProvider.End(MicrophoneDevice);
            microphoneClip = null;

            if (IsDebug) Debug.Log("Microphone stopped");
        }
# endif

        public void MuteMicrophone(bool mute)
        {
            IsMuted = mute;
            if (IsDebug) Debug.Log(mute ? "Microphone muted" : "Microphone unmuted");
        }

        public void SetNoiseGateThresholdDb(float db)
        {
            NoiseGateThresholdDb = db;
            UpdateLinearVolumes();
        }

        private void UpdateLinearVolumes()
        {
            linearNoiseGateThreshold = Mathf.Pow(10.0f, NoiseGateThresholdDb / 20.0f);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private float[] GetAmplitudeData()
        {
            var buffer = webGLSamplesBuffer.Count > 0 ? webGLSamplesBuffer.Dequeue() : new float[0];

            if (IsMuted || IsWebGLMicrophoneRecording() == 0)
            {
                return new float[0];
            }
            else
            {
                return buffer;
            }
        }

        // WebGL plugin sets sample data here
        private void SetSamplingData(string samplingDataString)
        {
            if (useMallocInWebGL)
            {
                // Get pointer and length of sampling data
                var sep = samplingDataString.IndexOf(":");
                var span = samplingDataString.AsSpan();
                var ptrSpan = span.Slice(0, sep);
                var lenSpan = span.Slice(sep + 1);
                if (!int.TryParse(ptrSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ptrInt) ||
                    !int.TryParse(lenSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int length) ||
                    length <= 0)
                {
                    return;
                }
                if (length > (int.MaxValue / sizeof(float))) return;

                // Buffers
                var bytes = length * sizeof(float);
                if (tempBytes == null || tempBytes.Length < bytes)
                {
                    tempBytes = new byte[Math.Max(bytes, 4096)];    // Min 4096 to reduce GC
                }
                var samplingData = new float[length];

                // Get data from malloc
                var src = new IntPtr(ptrInt);
                try
                {
                    Marshal.Copy(src, tempBytes, 0, bytes);     // WASM heap -> byte[]
                    Buffer.BlockCopy(tempBytes, 0, samplingData, 0, bytes); //byte[] -> float[]
                    webGLSamplesBuffer.Enqueue(samplingData);
                }
                finally
                {
                    JsFree(src);
                }
            }
            else
            {
                var samplingData = samplingDataString.Split(',').Select(s => Convert.ToSingle(s)).ToArray();
                webGLSamplesBuffer.Enqueue(samplingData);
            }
        }
#else
        private float[] GetAmplitudeData()
        {
            CurrentSamples = 0;

            if (IsMuted || microphoneClip == null)
            {
                return new float[0];
            }

            var currentPosition = MicrophoneProvider.GetPosition(MicrophoneDevice);
            if (currentPosition < 0 || currentPosition >= microphoneClip.samples)
            {
                Debug.LogWarning($"Invalid microphone position detected: {currentPosition} (samples={microphoneClip.samples})");
                return new float[0];
            }

            var sampleLength = (currentPosition >= lastSamplePosition) 
                ? currentPosition - lastSamplePosition 
                : microphoneClip.samples - lastSamplePosition + currentPosition;

            CurrentSamples = sampleLength;

            if (sampleLength <= 0)
            {
                return new float[0];
            }

            var samples = new float[sampleLength * microphoneClip.channels];

            try
            {
                if (currentPosition >= lastSamplePosition)
                {
                    microphoneClip.GetData(samples, lastSamplePosition);
                }
                else
                {
                    var endSamplesLength = (microphoneClip.samples - lastSamplePosition) * microphoneClip.channels;
                    var startSamplesLength = currentPosition * microphoneClip.channels;

                    if (endSamplesLength > 0)
                    {
                        var endSamples = new float[endSamplesLength];
                        microphoneClip.GetData(endSamples, lastSamplePosition);
                        endSamples.CopyTo(samples, 0);
                    }

                    if (startSamplesLength > 0)
                    {
                        var startSamples = new float[startSamplesLength];
                        microphoneClip.GetData(startSamples, 0);
                        startSamples.CopyTo(samples, endSamplesLength);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error while accessing microphone data: {ex.Message}");
                return new float[0];
            }

            lastSamplePosition = currentPosition;

            return samples;
        }
#endif

        // Manage recording session
        public void StartRecordingSession(RecordingSession session)
        {
            activeSessions.Add(session);
        }

        public void StopRecordingSession(RecordingSession session)
        {
            activeSessions.Remove(session);
        }

        private float GetCurrentMicVolumeDb(float[] samples)
        {
            if (samples.Length == 0)
            {
                return -Mathf.Infinity;
            }

            // Convert amp to db
            var sum = 0f;
            foreach (var sample in samples)
            {
                sum += sample * sample;
            }
            var rms = Mathf.Sqrt(sum / samples.Length);
            var db = 20f * Mathf.Log10(rms);

            if (float.IsNegativeInfinity(db) || float.IsNaN(db))
            {
                db = -Mathf.Infinity;
            }

            return db;
        }
    }
}

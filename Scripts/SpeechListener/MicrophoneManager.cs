using System.Collections.Generic;
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Linq;
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace ChatdollKit.SpeechListener
{
    public class MicrophoneManager : MonoBehaviour, IMicrophoneManager
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void InitWebGLMicrophone(string targetObjectName);
        [DllImport("__Internal")]
        private static extern void StartWebGLMicrophone();
        [DllImport("__Internal")]
        private static extern void EndWebGLMicrophone();
        [DllImport("__Internal")]
        private static extern int IsWebGLMicrophoneRecording();
        private Queue<float[]> webGLSamplesBuffer = new Queue<float[]>();
#endif
        public string MicrophoneDevice;
        public int SampleRate = 44100;
        public float NoiseGateThresholdDb = -50.0f;
        public bool AutoStart = true;
        public bool IsDebug = false;
        public float CurrentVolumeDb { get; private set; }

        public bool IsMuted { get; private set; } = false;
        private AudioClip microphoneClip;
        private int lastSamplePosition;
        private float linearNoiseGateThreshold;
        private List<RecordingSession> activeSessions = new List<RecordingSession>();

        private void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SampleRate = 44100;
            InitWebGLMicrophone(gameObject.name);
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

            foreach (var session in activeSessions)
            {
                session.ProcessSamples(samples, linearNoiseGateThreshold);
            }
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
            if (microphoneClip != null)
            {
                Debug.Log("Microphone already started");
                return;
            }

            if (MicrophoneDevice == null)
            {
                MicrophoneDevice = Microphone.devices[0];
            }

            microphoneClip = Microphone.Start(MicrophoneDevice, true, 1, SampleRate);
            lastSamplePosition = 0;

            if (IsDebug) Debug.Log("Microphone started");
        }

        public void StopMicrophone()
        {
            Microphone.End(MicrophoneDevice);
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
            if (IsMuted || IsWebGLMicrophoneRecording() == 0)
            {
                return new float[0];
            }

            if (webGLSamplesBuffer.Count > 0)
            {
                return webGLSamplesBuffer.Dequeue();
            }
            else
            {
                return new float[0];
            }
        }

        // WebGL plugin sets sample data here
        private void SetSamplingData(string samplingDataString)
        {
            var samplingData = samplingDataString.Split(',').Select(s => Convert.ToSingle(s)).ToArray();
            webGLSamplesBuffer.Enqueue(samplingData);
        }
#else
        private float[] GetAmplitudeData()
        {
            if (IsMuted || microphoneClip == null)
            {
                return new float[0];
            }

            var currentPosition = Microphone.GetPosition(MicrophoneDevice);
            if (currentPosition < 0 || currentPosition >= microphoneClip.samples)
            {
                Debug.LogWarning($"Invalid microphone position detected: {currentPosition}");
                return new float[0];
            }

            var sampleLength = (currentPosition >= lastSamplePosition) 
                ? currentPosition - lastSamplePosition 
                : microphoneClip.samples - lastSamplePosition + currentPosition;

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

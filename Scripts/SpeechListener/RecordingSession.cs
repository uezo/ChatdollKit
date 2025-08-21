using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChatdollKit.SpeechListener
{
    public class RecordingSession
    {
        public string Name;
        public float SilenceDurationThreshold;
        public float MinRecordingDuration;
        public float MaxRecordingDuration;
        public System.Action<float[]> OnRecordingComplete;
        public List<Func<float[], float, bool>> DetectVoiceFunctions;

        private List<float> recordedSamples = new List<float>();
        private float[] prerollBuffer;
        private int maxPrerollSamples;
        private int prerollIndex = 0;
        private int prerollCount = 0;
        public bool IsRecording { get; private set; }
        public bool IsSilent { get; private set; }
        private bool isCompleted = false;
        private float silenceDuration = 0.0f;
        private float recordingStartTime;

        public RecordingSession(string name, float silenceDurationThreshold, float minRecordingDuration, float maxRecordingDuration, int maxPrerollSamples, System.Action<float[]> onRecordingComplete, List<Func<float[], float, bool>> detectVoiceFunctions)
        {
            Name = name;
            SilenceDurationThreshold = silenceDurationThreshold;
            MinRecordingDuration = minRecordingDuration;
            MaxRecordingDuration = maxRecordingDuration;
            this.maxPrerollSamples = maxPrerollSamples;
            prerollBuffer = new float[maxPrerollSamples];
            OnRecordingComplete = onRecordingComplete;
            DetectVoiceFunctions = detectVoiceFunctions;
        }

        public void ProcessSamples(float[] samples, float linearNoiseGateThreshold)
        {
            if (isCompleted)
            {
                return; // Do not process completed session
            }

            // Check silence
            IsSilent = true;
            foreach (var f in DetectVoiceFunctions)
            {
                IsSilent = !f(samples, linearNoiseGateThreshold);
                if (IsSilent)
                {
                    break;
                }
            }

            if (!IsRecording && !IsSilent)
            {
                StartRecording();
            }

            if (IsRecording)
            {
                if (IsSilent)
                {
                    silenceDuration += Time.deltaTime;
                    if (silenceDuration >= SilenceDurationThreshold)
                    {
                        StopRecording();
                    }
                }
                else
                {
                    silenceDuration = 0.0f;
                }

                recordedSamples.AddRange(samples);

                if (Time.time - recordingStartTime > MaxRecordingDuration)
                {
                    StopRecording(invokeCallback: false);
                }
            }
            else
            {
                // Add samples to circular buffer
                foreach (var sample in samples)
                {
                    prerollBuffer[prerollIndex] = sample;
                    prerollIndex = (prerollIndex + 1) % maxPrerollSamples;
                    if (prerollCount < maxPrerollSamples)
                        prerollCount++;
                }
            }
        }

        private void StartRecording()
        {
            if (IsRecording || isCompleted)
            {
                return; // Do not start recording when session is already started or completed
            }

            IsRecording = true;
            silenceDuration = 0.0f;
            recordingStartTime = Time.time;
            recordedSamples.Clear();
        }

        private void StopRecording(bool invokeCallback = true)
        {
            if (!IsRecording || isCompleted)
            {
                return; // Do not stop recording when session is not started yet or already completed
            }

            IsRecording = false;

            if (invokeCallback)
            {
                var recordingDuration = Time.time - recordingStartTime - silenceDuration;
                if (recordingDuration >= MinRecordingDuration && recordingDuration <= MaxRecordingDuration)
                {
                    isCompleted = true; // Set isCompleted=true only when the length is valid
                    var combinedSamples = GetCombinedSamples();
                    OnRecordingComplete?.Invoke(combinedSamples);
                }
            }

            recordedSamples.Clear();
            prerollIndex = 0;
            prerollCount = 0;
        }
        
        private float[] GetCombinedSamples()
        {
            var prerollArray = new float[prerollCount];
            var startIndex = prerollCount < maxPrerollSamples ? 0 : prerollIndex;
            
            for (int i = 0; i < prerollCount; i++)
            {
                prerollArray[i] = prerollBuffer[(startIndex + i) % maxPrerollSamples];
            }
            
            var combinedSamples = new float[prerollCount + recordedSamples.Count];
            Array.Copy(prerollArray, 0, combinedSamples, 0, prerollCount);
            recordedSamples.CopyTo(combinedSamples, prerollCount);
            
            return combinedSamples;
        }
    }
}

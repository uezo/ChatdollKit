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

        private List<float> recordedSamples = new List<float>();
        private bool isRecording = false;
        private bool isCompleted = false;
        private float silenceDuration = 0.0f;
        private float recordingStartTime;

        public void ProcessSamples(float[] samples, float linearNoiseGateThreshold)
        {
            if (isCompleted)
            {
                return; // Do not process completed session
            }

            // Check silence
            var isSilent = true;
            for (var i = 0; i < samples.Length; i++)
            {
                if (Mathf.Abs(samples[i]) >= linearNoiseGateThreshold)
                {
                    isSilent = false;
                    break;
                }
            }
 
            if (!isRecording && !isSilent)
            {
                StartRecording();
            }

            if (isRecording)
            {
                if (isSilent)
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
        }

        private void StartRecording()
        {
            if (isRecording || isCompleted)
            {
                return; // Do not start recording when session is already started or completed
            }

            isRecording = true;
            silenceDuration = 0.0f;
            recordingStartTime = Time.time;
            recordedSamples.Clear();
        }

        private void StopRecording(bool invokeCallback = true)
        {           
            if (!isRecording || isCompleted)
            {
                return; // Do not stop recording when session is not started yet or already completed
            }

            isRecording = false;

            if (invokeCallback)
            {
                var recordingDuration = Time.time - recordingStartTime - silenceDuration;
                if (recordingDuration >= MinRecordingDuration && recordingDuration <= MaxRecordingDuration)
                {
                    isCompleted = true; // Set isCompleted=true only when the length is valid
                    OnRecordingComplete?.Invoke(recordedSamples.ToArray());
                }
            }

            recordedSamples.Clear();
        }
    }
}

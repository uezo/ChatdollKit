using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace ChatdollKit.IO
{
    public enum VoiceRecorderStatus { NotWorking, StartRequestAccepted, Listening, Recording, StopRequestAccepted }

    public class VoiceRecorder : MonoBehaviour
    {
        // Data
        private AudioClip microphoneInput;
        private List<float> recordedData;
        private float[] samplingData;
        private int previousPosition;
        private float silenceStartTime;

        // Status
        public VoiceRecorderStatus Status { get; private set; }
        public bool IsListening { get; private set; }
        public bool IsRecording { get; private set; }

        // Configurations
        public int SamplingFrequency = 16000;
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionMinimumLength = 0.3f;
        public float SilenceDurationToEndRecording = 1.0f;
        public float RecordingStartTimeout = 5.0f;
        public float ListeningTimeout = 20.0f;
        public bool StopListeningOnDetectionEnd = false;
        public Action OnListeningStart = () => { Debug.Log("Listening start"); };
        public Action OnListeningStop = () => { Debug.Log("Listening stopped"); };
        public Action OnRecordingStart = () => { Debug.Log("Recording start"); };
        public Action OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd = (a) => { Debug.Log($"Recording end: {a.length}"); };
        public Action<float> OnListeningTimeout = (s) => { Debug.Log($"Listening timeout: {s} sec"); };
        public Action<float> OnRecordingStartTimeout = (s) => { Debug.Log($"Recording start timeout: {s} sec"); };
        public Action<Exception> OnError = (e) => { Debug.LogError($"VoiceRecording error: {e.Message}\n{e.StackTrace}"); };

        // Runtime configurations (updated on StartListening)
        private int samplingFrequency;
        private float voiceDetectionThreshold;
        private float silenceDurationToEndRecording;
        private float voiceDetectionMinimumLength;
        private bool stopListeningOnDetectionEnd;
        private float listeningTimeout;
        private float recordingStartTimeout;
        private float listeningStartTime;
        private Action onListeningStart;
        private Action onListeningStop;
        private Action onRecordingStart;
        private Action onDetectVoice;
        private Action<AudioClip> onRecordingEnd;
        private Action<float> onListeningTimeout;
        private Action<float> onRecordingStartTimeout;
        private Action<Exception> onError;

        // Request flags
        private bool startListeningRequested = false;
        private bool stopListeningRequested = false;


        private void Update()
        {
            // Initialize and start listening
            if (startListeningRequested)
            {
                startListeningRequested = false;
                StartListening();
            }

            // Finalize and stop listening
            if (stopListeningRequested)
            {
                stopListeningRequested = false;
                StopListening();
            }

            // Return if not listening
            if (!IsListening)
            {
                return;
            }

            try
            {
                if (listeningTimeout > 0.0f)
                {
                    // Timeout when listening duration is longer than timeout value
                    var listeningDuration = Time.time - listeningStartTime;
                    if (listeningDuration > listeningTimeout)
                    {
                        StopListening();
                        onListeningTimeout?.Invoke(listeningDuration);
                        return;
                    }
                }

                if (recordingStartTimeout > 0.0f && silenceStartTime == 0.0f)
                {
                    // Timeout when keep silent from start
                    var silenceDurationFromStart = Time.time - listeningStartTime;
                    if (silenceDurationFromStart > recordingStartTimeout)
                    {
                        StopListening();
                        onRecordingStartTimeout?.Invoke(silenceDurationFromStart);
                        return;
                    }
                }

                // Get position
                var currentPosition = Microphone.GetPosition(null);
                if (currentPosition < 0 || previousPosition == currentPosition)
                {
                    return;
                }

                // Get sampling data from microphone
                microphoneInput.GetData(samplingData, 0);

                // Add captured data
                float[] capturedData;
                if (currentPosition > previousPosition)
                {
                    capturedData = new float[currentPosition - previousPosition];
                    Array.Copy(samplingData, previousPosition, capturedData, 0, currentPosition - previousPosition);
                }
                else
                {
                    // When the data is located at the end and head of sampling frames
                    capturedData = new float[samplingData.Length - previousPosition + currentPosition];
                    Array.Copy(samplingData, previousPosition, capturedData, 0, samplingData.Length - previousPosition);
                    Array.Copy(samplingData, 0, capturedData, samplingData.Length - previousPosition, currentPosition);
                }
                recordedData.AddRange(capturedData);

                // Update previous position for next Update
                previousPosition = currentPosition;

                // Handle recorded voice
                if (capturedData.Max(d => Math.Abs(d)) > voiceDetectionThreshold)
                {
                    onDetectVoice?.Invoke();

                    // Start or continue recording when the volume of captured sound is larger than threshold
                    if (!IsRecording)
                    {
                        // Invoke recording start event handler
                        onRecordingStart?.Invoke();
                    }
                    IsRecording = true;
                    Status = VoiceRecorderStatus.Recording;
                    silenceStartTime = Time.time;
                }
                else
                {
                    if (IsRecording)
                    {
                        if (Time.time - silenceStartTime >= silenceDurationToEndRecording)
                        {
                            // End recording when silence is longer than configured duration
                            IsRecording = false;

                            var recordedLength = recordedData.Count / (float)(microphoneInput.frequency * microphoneInput.channels) - silenceDurationToEndRecording;
                            if (recordedLength >= voiceDetectionMinimumLength)
                            {
                                // Create AudioClip and copy recorded data
                                var audioClip = AudioClip.Create(string.Empty, recordedData.Count, microphoneInput.channels, microphoneInput.frequency, false);
                                audioClip.SetData(recordedData.ToArray(), 0);

                                // Invoke event handler
                                onRecordingEnd?.Invoke(audioClip);

                                // Stop listening
                                if (stopListeningOnDetectionEnd)
                                {
                                    StopListening();
                                }
                            }
                            recordedData.Clear();
                        }
                    }
                    else
                    {
                        // Keep clean when not recording
                        recordedData.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                StopListening();
                onError?.Invoke(ex);
            }
        }

        private void OnDestroy()
        {
            StopListening();
        }

        public void StartRecorder()
        {
            startListeningRequested = true;
            Status = VoiceRecorderStatus.StartRequestAccepted;
        }

        private void StartListening()
        {
            // Update runtime configurations
            samplingFrequency = SamplingFrequency;
            voiceDetectionThreshold = VoiceDetectionThreshold;
            silenceDurationToEndRecording = SilenceDurationToEndRecording;
            voiceDetectionMinimumLength = VoiceDetectionMinimumLength;
            stopListeningOnDetectionEnd = StopListeningOnDetectionEnd;
            onListeningStart = OnListeningStart;
            onListeningStop = OnListeningStop;
            onListeningTimeout = OnListeningTimeout;
            onRecordingStartTimeout = OnRecordingStartTimeout;
            onDetectVoice = OnDetectVoice;
            onRecordingStart = OnRecordingStart;
            onRecordingEnd = OnRecordingEnd;
            onError = OnError;
            listeningTimeout = ListeningTimeout;
            recordingStartTimeout = RecordingStartTimeout;

            // (Re)start microphone
            if (Microphone.IsRecording(null))
            {
                Microphone.End(null);
            }
            microphoneInput = Microphone.Start(null, true, 1, samplingFrequency);

            // Initialize data and status
            recordedData = new List<float>();
            samplingData = new float[microphoneInput.samples * microphoneInput.channels];
            previousPosition = 0;
            IsRecording = false;
            listeningStartTime = Time.time;

            // Recognized as listening started
            IsListening = true;
            Status = VoiceRecorderStatus.Listening;
            onListeningStart?.Invoke();
        }

        public void StopRecorder()
        {
            stopListeningRequested = true;
            Status = VoiceRecorderStatus.StopRequestAccepted;
        }

        private void StopListening()
        {
            // Stop microphone
            Microphone.End(null);

            // Clear data
            recordedData?.Clear();
            if (samplingData != null)
            {
                Array.Clear(samplingData, 0, samplingData.Length);
            }

            // Recognized as listening stopped
            IsListening = false;
            Status = VoiceRecorderStatus.NotWorking;
            onListeningStop?.Invoke();
        }
    }
}

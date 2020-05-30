using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private VoiceRecorderResponse lastRecordedVoice;

        // Status and timestamp
        public VoiceRecorderStatus Status { get; private set; }
        public bool IsListening { get; private set; }
        public bool IsRecording { get; private set; }
        public bool IsEnabled;
        private float lastVoiceDetectedTime;
        private float recordingStartTime;

        // Runtime configurations
        private int samplingFrequency;
        private float voiceDetectionThreshold;
        private float voiceDetectionMinimumLength;
        private float silenceDurationToEndRecording;
        private Action onListeningStart;
        private Action onListeningStop;
        private Action onRecordingStart;
        private Action<float> onDetectVoice;
        private Action<AudioClip> onRecordingEnd;
        private Action<Exception> onError;

        // Request data
        private VoiceRecorderRequest voiceRecorderRequest;

        private void Start()
        {
            voiceRecorderRequest = new VoiceRecorderRequest();
            Configure();
            StartListening();
        }

        private void Update()
        {
            // Apply configuration
            if (voiceRecorderRequest != null)
            {
                Configure();
            }

            // Return if disabled or not listening
            if (!IsEnabled || !IsListening)
            {
                return;
            }

            try
            {
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
                var maxVolume = capturedData.Max(d => Math.Abs(d));
                if (maxVolume > voiceDetectionThreshold)
                {
                    onDetectVoice?.Invoke(maxVolume);

                    // Start or continue recording when the volume of captured sound is larger than threshold
                    if (!IsRecording)
                    {
                        // Set recording starttime
                        recordingStartTime = Time.time;
                        // Invoke recording start event handler
                        onRecordingStart?.Invoke();
                    }
                    IsRecording = true;
                    Status = VoiceRecorderStatus.Recording;
                    lastVoiceDetectedTime = Time.time;
                }
                else
                {
                    if (IsRecording)
                    {
                        if (Time.time - lastVoiceDetectedTime >= silenceDurationToEndRecording)
                        {
                            // End recording when silence is longer than configured duration
                            IsRecording = false;

                            var recordedLength = recordedData.Count / (float)(microphoneInput.frequency * microphoneInput.channels) - silenceDurationToEndRecording;
                            if (recordedLength >= voiceDetectionMinimumLength)
                            {
                                // Create AudioClip and copy recorded data
                                var audioClip = AudioClip.Create(string.Empty, recordedData.Count, microphoneInput.channels, microphoneInput.frequency, false);
                                audioClip.SetData(recordedData.ToArray(), 0);

                                // Give reference of audio clip to all audiences
                                lastRecordedVoice = new VoiceRecorderResponse() { RecordingStartedAt = recordingStartTime, Voice = audioClip };

                                onRecordingEnd?.Invoke(audioClip);
                            }
                            else
                            {
                                onRecordingEnd?.Invoke(null);
                            }
                            recordedData.Clear();
                        }
                    }
                    else
                    {
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

        private void Configure()
        {
            samplingFrequency = voiceRecorderRequest.SamplingFrequency;
            voiceDetectionThreshold = voiceRecorderRequest.VoiceDetectionThreshold;
            voiceDetectionMinimumLength = voiceRecorderRequest.VoiceDetectionMinimumLength;
            silenceDurationToEndRecording = voiceRecorderRequest.SilenceDurationToEndRecording;
            onListeningStart = voiceRecorderRequest.OnListeningStart;
            onListeningStop = voiceRecorderRequest.OnListeningStop;
            onRecordingStart = voiceRecorderRequest.OnRecordingStart;
            onDetectVoice = voiceRecorderRequest.OnDetectVoice;
            onRecordingEnd = voiceRecorderRequest.OnRecordingEnd;
            onError = voiceRecorderRequest.OnError;
            voiceRecorderRequest = null;
        }

        private async Task ConfigureAsync(VoiceRecorderRequest voiceRecorderRequest, CancellationToken token)
        {
            this.voiceRecorderRequest = voiceRecorderRequest;

            // Wait for configuration applied (this.voiceRecorderRequest should be null after config applied)
            while (this.voiceRecorderRequest != null && !token.IsCancellationRequested)
            {
                await Task.Delay(10);
            }
        }

        private void StartListening()
        {
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
            lastVoiceDetectedTime = 0.0f;

            // Recognized as listening started
            Status = VoiceRecorderStatus.Listening;
            IsListening = true;
            onListeningStart?.Invoke();
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

        public async Task RestartAsync(VoiceRecorderRequest voiceRecorderRequest, CancellationToken token)
        {
            // Stop before start
            StopListening();

            // Configure
            await ConfigureAsync(voiceRecorderRequest, token);

            // Start after configuration finished
            StartListening();
        }

        public async Task<VoiceRecorderResponse> GetVoiceAsync(VoiceRecorderRequest voiceRecorderRequest, CancellationToken token)
        {
            try
            {
                // Update configuration before recording start
                await ConfigureAsync(voiceRecorderRequest, token);

                var requestTimestamp = Time.time;

                IsEnabled = true;

                // Wait for voice recorded or timeout
                while (true)
                {
                    if (Status == VoiceRecorderStatus.NotWorking || token.IsCancellationRequested)
                    {
                        break;
                    }
                    if (lastRecordedVoice != null && lastRecordedVoice.RecordingStartedAt > requestTimestamp)
                    {
                        return lastRecordedVoice;
                    }
                    await Task.Delay(10);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured while getting voice from recorder: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                IsEnabled = false;
            }

            return null;
        }
    }

    public class VoiceRecorderRequest
    {
        public int SamplingFrequency = 16000;
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionMinimumLength = 0.3f;
        public float SilenceDurationToEndRecording = 1.0f;
        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart;
        public Action<float> OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd;
        public Action<Exception> OnError;
    }

    public class VoiceRecorderResponse
    {
        public float RecordingStartedAt { get; set; }
        public AudioClip Voice { get; set; }
    }
}

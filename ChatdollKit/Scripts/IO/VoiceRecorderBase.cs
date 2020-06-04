using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;


namespace ChatdollKit.IO
{
    [RequireComponent(typeof(ChatdollMicrophone))]
    public class VoiceRecorderBase : MonoBehaviour
    {
        private List<float> recordedData;
        private VoiceRecorderResponse lastRecordedVoice;
        private ChatdollMicrophone microphone;

        // Status and timestamp
        public bool IsListening { get; private set; }
        public bool IsRecording { get; private set; }
        public bool IsEnabled;
        private float lastVoiceDetectedTime;
        private float recordingStartTime;

        // Runtime configurations
        protected float voiceDetectionThreshold;
        protected float voiceDetectionMinimumLength;
        protected float silenceDurationToEndRecording;
        protected Action onListeningStart;
        protected Action onListeningStop;
        protected Action onRecordingStart;
        protected Action<float> onDetectVoice;
        protected Action<AudioClip> onRecordingEnd;
        protected Action<Exception> onError;

        private void LateUpdate()
        {
            // Return if disabled or not listening
            if (!IsEnabled || !IsListening)
            {
                return;
            }

            try
            {
                if (!microphone.IsUpdatedOnThisFrame)
                {
                    return;
                }

                // Get captured data from microphone
                var capturedData = microphone.CapturedData;

                // Handle recorded voice
                recordedData.AddRange(capturedData.Data);
                if (capturedData.MaxVolume > voiceDetectionThreshold)
                {
                    onDetectVoice?.Invoke(capturedData.MaxVolume);

                    // Start or continue recording when the volume of captured sound is larger than threshold
                    if (!IsRecording)
                    {
                        // Set recording starttime
                        recordingStartTime = Time.time;
                        // Invoke recording start event handler
                        onRecordingStart?.Invoke();
                    }
                    IsRecording = true;
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

                            var recordedLength = recordedData.Count / (float)(capturedData.Frequency * capturedData.ChannelCount) - silenceDurationToEndRecording;
                            if (recordedLength >= voiceDetectionMinimumLength)
                            {
                                // Create AudioClip and copy recorded data
                                var audioClip = AudioClip.Create(string.Empty, recordedData.Count, capturedData.ChannelCount, capturedData.Frequency, false);
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

        protected virtual void OnDestroy()
        {
            StopListening();
        }

        public void StartListening()
        {
            microphone = gameObject.GetComponent<ChatdollMicrophone>();

            // Initialize data and status
            recordedData = new List<float>();
            IsRecording = false;
            lastVoiceDetectedTime = 0.0f;

            // Recognized as listening started
            IsListening = true;
            onListeningStart?.Invoke();
        }

        public void StopListening()
        {
            // Clear data
            recordedData?.Clear();

            // Recognized as listening stopped
            IsListening = false;
            onListeningStop?.Invoke();
        }

        public async Task<VoiceRecorderResponse> GetVoiceAsync(float timeout, CancellationToken token)
        {
            try
            {
                var requestTimestamp = Time.time;
                IsEnabled = true;

                // Wait for voice recorded or timeout
                while (true)
                {
                    if (!IsListening || token.IsCancellationRequested)
                    {
                        break;  // Recorder stopped or request canceled
                    }
                    if (timeout > 0.0f && Time.time - requestTimestamp > timeout)
                    {
                        break; // Timeout
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

    public class VoiceRecorderResponse
    {
        public float RecordingStartedAt { get; set; }
        public AudioClip Voice { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

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
        public bool IsDetectingVoice { get; private set; }
        public bool IsEnabled;
        private float lastVoiceDetectedTime;
        private float recordingStartTime;

        // Runtime configurations
        [Header("Voice Recorder Settings")]
        public float VoiceDetectionThreshold = -50.0f;
        public float VoiceDetectionMinimumLength = 0.3f;
        public float SilenceDurationToEndRecording = 1.0f;
        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart;
        public Action<float> OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd;
        public Action<Exception> OnError;

        // Microphone controller
        public Func<bool> UnmuteOnListeningStart { get; set; } = () => { return true; };
        public Func<bool> MuteOnListeningStop { get; set; } = () => { return true; };

        // Testing and debugging
        public string TextInput { get; set; }

        private void LateUpdate()
        {
            // Return if disabled or not listening
            if (!IsEnabled || !IsListening || !microphone.IsListening)
            {
                return;
            }

            try
            {
                // Get captured data from microphone
                var capturedData = microphone.CapturedData;

                // Handle recorded voice
                recordedData.AddRange(capturedData.Data);

                if (capturedData.Volume > VoiceDetectionThreshold)
                {
                    IsDetectingVoice = true;
                    OnDetectVoice?.Invoke(capturedData.Volume);

                    // Start or continue recording when the volume of captured sound is larger than threshold
                    if (!IsRecording)
                    {
                        // Set recording starttime
                        recordingStartTime = Time.time;
                        // Invoke recording start event handler
                        (OnRecordingStart ?? OnRecordingStartDefault).Invoke();
                    }
                    IsRecording = true;
                    lastVoiceDetectedTime = Time.time;
                }
                else
                {
                    IsDetectingVoice = false;
                    if (IsRecording)
                    {
                        if (Time.time - lastVoiceDetectedTime >= SilenceDurationToEndRecording)
                        {
                            // End recording when silence is longer than configured duration
                            IsRecording = false;

                            var recordedLength = recordedData.Count / (float)(capturedData.Frequency * capturedData.ChannelCount) - SilenceDurationToEndRecording;
                            if (recordedLength >= VoiceDetectionMinimumLength)
                            {
                                lastRecordedVoice = new VoiceRecorderResponse(recordingStartTime, recordedData, capturedData.ChannelCount, capturedData.Frequency);
                                (OnRecordingEnd ?? OnRecordingEndDefault).Invoke(lastRecordedVoice.Voice);
                            }
                            else
                            {
                                (OnRecordingEnd ?? OnRecordingEndDefault).Invoke(null);
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
                (OnError ?? OnErrorDefault).Invoke(ex);
            }
        }

        protected virtual void OnDestroy()
        {
            StopListening();
        }

        public void StartListening()
        {
            // Unmute microphone on start
            microphone = gameObject.GetComponent<ChatdollMicrophone>();
            if (UnmuteOnListeningStart())
            {
                microphone.IsMuted = false;
            }

            // Initialize data and status
            recordedData = new List<float>();
            IsRecording = false;
            lastVoiceDetectedTime = 0.0f;

            // Recognized as listening started
            IsListening = true;
            OnListeningStart?.Invoke();
        }

        public void StopListening()
        {
            // Mute microphone on end
            microphone = gameObject.GetComponent<ChatdollMicrophone>();
            if (MuteOnListeningStop())
            {
                microphone.IsMuted = true;
            }

            // Clear data
            recordedData?.Clear();

            // Recognized as listening stopped
            IsListening = false;
            OnListeningStop?.Invoke();
        }

        public async UniTask<VoiceRecorderResponse> GetVoiceAsync(float timeout, CancellationToken token)
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
                    if (!string.IsNullOrEmpty(TextInput))
                    {
                        var response = new VoiceRecorderResponse(TextInput);
                        TextInput = string.Empty;
                        return response;
                    }
                    if (lastRecordedVoice != null && lastRecordedVoice.RecordingStartedAt > requestTimestamp)
                    {
                        return lastRecordedVoice;
                    }
                    await UniTask.Delay(10);
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

        protected virtual void OnRecordingStartDefault()
        {
            Debug.Log($"Recording start: {gameObject.name}");
        }

        protected virtual void OnRecordingEndDefault(AudioClip audioClip)
        {
            Debug.Log($"Recording end: {gameObject.name}");
        }

        protected virtual void OnErrorDefault(Exception ex)
        {
            Debug.LogError($"Recording error at {gameObject.name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public class VoiceRecorderResponse
    {
        public float RecordingStartedAt { get; set; }
        public AudioClip Voice { get; set; }
        public float[] SamplingData { get; set; }
        public string Text { get; set; }

        public VoiceRecorderResponse(float recordingStartedAt, List<float> samplingDataList, int channelCount, int frequency)
        {
            RecordingStartedAt = recordingStartedAt;
            SamplingData = samplingDataList.ToArray();
            Voice = AudioClip.Create(string.Empty, samplingDataList.Count, channelCount, frequency, false);
            Voice.SetData(SamplingData, 0);
        }

        public VoiceRecorderResponse(string text)
        {
            Text = text;
        }
    }
}

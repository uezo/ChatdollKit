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
        protected float voiceDetectionThreshold;
        protected float voiceDetectionMaxThreshold = 0.7f;
        protected float voiceDetectionMinimumLength;
        protected float silenceDurationToEndRecording;
        protected Action onListeningStart;
        protected Action onListeningStop;
        protected Action onRecordingStart;
        protected Action<float> onDetectVoice;
        protected Action<AudioClip> onRecordingEnd;
        protected Action<Exception> onError;

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
                if (capturedData.MaxVolume > voiceDetectionThreshold && capturedData.MaxVolume < voiceDetectionMaxThreshold)
                {
                    IsDetectingVoice = true;
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
                    IsDetectingVoice = false;
                    if (IsRecording)
                    {
                        if (Time.time - lastVoiceDetectedTime >= silenceDurationToEndRecording)
                        {
                            // End recording when silence is longer than configured duration
                            IsRecording = false;

                            var recordedLength = recordedData.Count / (float)(capturedData.Frequency * capturedData.ChannelCount) - silenceDurationToEndRecording;
                            if (recordedLength >= voiceDetectionMinimumLength)
                            {
                                lastRecordedVoice = new VoiceRecorderResponse(recordingStartTime, recordedData, capturedData.ChannelCount, capturedData.Frequency);
                                onRecordingEnd?.Invoke(lastRecordedVoice.Voice);
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
            onListeningStart?.Invoke();
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
            onListeningStop?.Invoke();
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

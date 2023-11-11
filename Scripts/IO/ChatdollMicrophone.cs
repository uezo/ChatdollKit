using System;
using System.Linq;
using UnityEngine;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif
# if UNITY_WEBGL && !UNITY_EDITOR
using Microphone = ChatdollKit.IO.WebGLMicrophone;
# else
using Microphone = UnityEngine.Microphone;
#endif

namespace ChatdollKit.IO
{
    public class ChatdollMicrophone : MonoBehaviour
    {
        public bool IsMicrophoneEnabled { get; private set; } = false;

        // Data
        private AudioClip microphoneInput;
        private float[] samplingData;
        private int previousPosition;
        public MicrophoneCapturedData CapturedData { get; private set; }

        // Status and timestamp
        public bool IsListening;
        public bool IsEnabled = true;

        // Microphone device
        public string DeviceName;
        private string listeningDeviceName;

        // Runtime configurations
        public int SamplingFrequency = 16000;

        // Mute
        public bool IsMuted { get; private set; } = false;

        // Debug
        public bool DebugMicrophone = false;
        public bool DebugSamplingData = false;

        private void Awake()
        {
#if PLATFORM_ANDROID
            // Request permission if Android
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
            }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
            if (gameObject.GetComponent<Microphone>() == null)
            {
                gameObject.AddComponent<Microphone>();
            }
#endif
        }

        private void Start()
        {
            if (SamplingFrequency > 0)
            {
                StartListening();
            }
        }

        private void Update()
        {
            CapturedData = new MicrophoneCapturedData(microphoneInput.channels, microphoneInput.frequency);

            if (DebugSamplingData)
            {
                microphoneInput.GetData(samplingData, 0);
                Debug.Log("Samples from device: " + string.Join(",", samplingData));
            }

            if (!IsMicrophoneEnabled)
            {
#if PLATFORM_ANDROID
                // Check permission if Android
                if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
                {
                    IsMicrophoneEnabled = true;
                }
                else
                {
                    return;
                }
#else
                IsMicrophoneEnabled = true;
#endif
                Debug.Log("Permission for microphone is granted");
            }

            // Return if disabled or not listening
            if (!IsEnabled || !IsListening)
            {
                if (DebugMicrophone)
                {
                    Debug.Log($"Microphone is not listening: {IsListening} (IsEnabled: {IsEnabled})");
                }
                return;
            }

            try
            {
                // Get position
                var currentPosition = Microphone.GetPosition(listeningDeviceName);
                if (currentPosition < 0 || previousPosition == currentPosition)
                {
                    if (DebugMicrophone)
                    {
                        Debug.Log($"Microphone position is negative or not changed: {currentPosition} / {previousPosition == currentPosition})");
                    }
                    return;
                }

                // Get sampling data from microphone
                microphoneInput.GetData(samplingData, 0);

                if (DebugSamplingData)
                {
                    Debug.Log("Samples listened: " + string.Join(",", samplingData));
                }

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

                // Update previous position for next Update
                previousPosition = currentPosition;

                CapturedData.SetData(capturedData);
            }
            catch (Exception ex)
            {
                StopListening();
                Debug.LogError($"ChatdollMicrophone stopped: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnDestroy()
        {
            Debug.LogWarning("Microphone destroyed. Stop listening.");
            StopListening();
        }

        public void SetMute(bool mute)
        {
            if (mute)
            {
                Debug.Log("Microphone muted. Stop listening.");
                StopListening();
            }
            else
            {
                Debug.Log("Microphone unmuted. Start listening.");
                if (SamplingFrequency > 0)
                {
                    StartListening();
                }
            }

            IsMuted = mute;
        }

        public void ToggleMute()
        {
            SetMute(!IsMuted);
        }

        public void ChangeInputDevice(string deviceName)
        {
            DeviceName = deviceName;
            StartListening();
        }

        public void StartListening()
        {
            // (Re)start microphone
            if (Microphone.IsRecording(listeningDeviceName))
            {
                Microphone.End(listeningDeviceName);
            }
            listeningDeviceName = DeviceName == string.Empty ? null : DeviceName;
            microphoneInput = Microphone.Start(listeningDeviceName, true, 1, SamplingFrequency);
            // Initialize data and status
            samplingData = new float[microphoneInput.samples * microphoneInput.channels];
            previousPosition = 0;
            // Assure CapturedData.data is set when IsListening is true
            CapturedData = new MicrophoneCapturedData(microphoneInput.channels, microphoneInput.frequency);
            IsListening = true;
        }

        public void StopListening()
        {
            IsListening = false;

            // Stop microphone
            Microphone.End(listeningDeviceName);

            // Clear data
            if (samplingData != null)
            {
                Array.Clear(samplingData, 0, samplingData.Length);
            }
        }
    }

    public class MicrophoneCapturedData
    {
        public int ChannelCount { get; }
        public int Frequency { get; }
        public float MaxVolume { get; private set; }
        public float[] Data { get; private set; }

        public MicrophoneCapturedData(int channelCount, int frequency)
        {
            ChannelCount = channelCount;
            Frequency = frequency;
            MaxVolume = 0;
            Data = new float[] { };
        }

        public void SetData(float[] capturedData)
        {
            MaxVolume = capturedData.Length > 0 ? capturedData.Max(d => Math.Abs(d)) : 0;
            Data = capturedData;
        }
    }
}

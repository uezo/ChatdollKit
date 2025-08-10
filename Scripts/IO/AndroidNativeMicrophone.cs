#if UNITY_ANDROID

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ChatdollKit.IO
{
    public static class AndroidNativeMicrophone
    {
        private const string PLUGIN_NAME = "androidnativemicrophone";

        [DllImport(PLUGIN_NAME)]
        private static extern int AndroidNativeMicrophonePlugin_GetDeviceCount();
        [DllImport(PLUGIN_NAME)]
        private static extern IntPtr AndroidNativeMicrophonePlugin_GetDeviceName(int index);
        [DllImport(PLUGIN_NAME)]
        private static extern int AndroidNativeMicrophonePlugin_Start(string deviceName, int lengthSec, int frequency);
        [DllImport(PLUGIN_NAME)]
        private static extern int AndroidNativeMicrophonePlugin_StartWithVoiceProcessing(string deviceName, int lengthSec, int frequency, int enableVoiceProcessing);
        [DllImport(PLUGIN_NAME)]
        private static extern void AndroidNativeMicrophonePlugin_End();
        [DllImport(PLUGIN_NAME)]
        private static extern int AndroidNativeMicrophonePlugin_IsRecording();
        [DllImport(PLUGIN_NAME)]
        private static extern int AndroidNativeMicrophonePlugin_GetPosition();
        [DllImport(PLUGIN_NAME)]
        private static extern IntPtr AndroidNativeMicrophonePlugin_GetAudioData();
        [DllImport(PLUGIN_NAME)]
        private static extern void AndroidNativeMicrophonePlugin_ForceReset();

        private static AudioClip currentClip;
        private static bool isCurrentlyRecording = false;
        private static string currentDevice = null;
        private static float[] pluginAudioBuffer;
        private static int bufferSize;
        private static int sampleRate;
        private static bool voiceProcessingEnabled = false;

        // Enable/disable debug logging
        private const bool DEBUG_LOG_ENABLED = true;

        private static void DebugLog(string message)
        {
            if (DEBUG_LOG_ENABLED)
            {
                Debug.Log($"[AndroidNativeMicrophone] {message}");
            }
        }

        private static void DebugLogError(string message)
        {
            Debug.LogError($"[AndroidNativeMicrophone] {message}");
        }

        /// <summary>
        /// Get all available audio input devices
        /// </summary>
        public static string[] devices
        {
            get
            {
#if !UNITY_EDITOR
                    int count = AndroidNativeMicrophonePlugin_GetDeviceCount();
                    string[] deviceNames = new string[count];

                    for (int i = 0; i < count; i++)
                    {
                        IntPtr namePtr = AndroidNativeMicrophonePlugin_GetDeviceName(i);
                        deviceNames[i] = Marshal.PtrToStringAnsi(namePtr);
                    }

                    return deviceNames;
#else
                // Return Unity's default devices when not on Android
                return Microphone.devices;
#endif
            }
        }

        /// <summary>
        /// Start recording with default voice processing enabled (echo cancellation ON)
        /// </summary>
        public static AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency)
        {
            return Start(deviceName, loop, lengthSec, frequency, true);
        }

        /// <summary>
        /// Start recording with optional voice processing
        /// </summary>
        /// <param name="deviceName">Device name or null for default</param>
        /// <param name="loop">Loop recording (not used in current implementation)</param>
        /// <param name="lengthSec">Recording buffer length in seconds</param>
        /// <param name="frequency">Sample rate in Hz (16000 or 48000 recommended)</param>
        /// <param name="enableVoiceProcessing">Enable Android voice processing (echo cancellation, noise reduction, AGC)</param>
        public static AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency, bool enableVoiceProcessing)
        {
#if !UNITY_EDITOR
                // Force cleanup if already recording
                if (isCurrentlyRecording)
                {
                    ForceReset();
                }

                string targetDevice = string.IsNullOrEmpty(deviceName) ? "Default" : deviceName;

                DebugLog($"Starting recording: device={targetDevice}, length={lengthSec}s, freq={frequency}Hz, voiceProcessing={enableVoiceProcessing}");

                // Call appropriate native function based on voice processing setting
                int result = enableVoiceProcessing
                    ? AndroidNativeMicrophonePlugin_StartWithVoiceProcessing(targetDevice, lengthSec, frequency, 1)
                    : AndroidNativeMicrophonePlugin_Start(targetDevice, lengthSec, frequency);

                if (result == 1)
                {
                    // Create Unity AudioClip as container
                    currentClip = AudioClip.Create("MicrophoneClip", lengthSec * frequency, 1, frequency, false);
                    
                    // IMPORTANT: Set HideFlags to prevent serialization issues in Editor
                    currentClip.hideFlags = HideFlags.DontSave;
                    
                    isCurrentlyRecording = true;
                    currentDevice = targetDevice;
                    bufferSize = lengthSec * frequency;
                    sampleRate = frequency;
                    voiceProcessingEnabled = enableVoiceProcessing;

                    // Allocate managed buffer for audio data transfer
                    pluginAudioBuffer = new float[bufferSize];

                    DebugLog($"Recording started successfully. BufferSize: {bufferSize}, VoiceProcessing: {enableVoiceProcessing}");
                    return currentClip;
                }
                else
                {
                    DebugLogError($"Failed to start recording. Result: {result}");
                    return null;
                }
#else
            // Fallback to Unity's Microphone on non-Android platforms
            DebugLog("Using Unity's built-in Microphone (not on Android device)");
            return Microphone.Start(deviceName, loop, lengthSec, frequency);
#endif
        }

        /// <summary>
        /// Stop recording on specified device
        /// </summary>
        public static void End(string deviceName)
        {
#if !UNITY_EDITOR
                if (!isCurrentlyRecording)
                    return;

                // Check if we're ending the correct device
                if (!string.IsNullOrEmpty(deviceName) && deviceName != currentDevice)
                    return;

                DebugLog("Ending recording");
                AndroidNativeMicrophonePlugin_End();

                // Properly destroy AudioClip
                if (currentClip != null)
                {
                    UnityEngine.Object.Destroy(currentClip);
                    currentClip = null;
                }

                // Clear state
                isCurrentlyRecording = false;
                currentDevice = null;
                pluginAudioBuffer = null;
                voiceProcessingEnabled = false;
#else
            // Fallback to Unity's Microphone
            Microphone.End(deviceName);
#endif
        }

        /// <summary>
        /// Check if currently recording on specified device
        /// </summary>
        public static bool IsRecording(string deviceName)
        {
#if !UNITY_EDITOR
                if (!isCurrentlyRecording)
                    return false;

                // Check device match
                if (!string.IsNullOrEmpty(deviceName) && deviceName != currentDevice)
                    return false;

                // Verify with native plugin
                bool pluginIsRecording = AndroidNativeMicrophonePlugin_IsRecording() == 1;

                if (!pluginIsRecording)
                {
                    isCurrentlyRecording = false;
                    return false;
                }

                // Update AudioClip with latest data
                UpdateAudioClip();
                return true;
#else
            // Fallback to Unity's Microphone
            return Microphone.IsRecording(deviceName);
#endif
        }

        /// <summary>
        /// Get current recording position in samples
        /// </summary>
        public static int GetPosition(string deviceName)
        {
#if !UNITY_EDITOR
                if (!IsRecording(deviceName))
                    return 0;

                return AndroidNativeMicrophonePlugin_GetPosition();
#else
            // Fallback to Unity's Microphone
            return Microphone.GetPosition(deviceName);
#endif
        }

        /// <summary>
        /// Transfer audio data from native plugin to Unity AudioClip
        /// </summary>
        private static void UpdateAudioClip()
        {
#if !UNITY_EDITOR
                if (currentClip == null || pluginAudioBuffer == null)
                    return;

                IntPtr dataPtr = AndroidNativeMicrophonePlugin_GetAudioData();
                if (dataPtr == IntPtr.Zero)
                    return;

                try
                {
                    // Copy native audio buffer to managed array
                    Marshal.Copy(dataPtr, pluginAudioBuffer, 0, bufferSize);

                    // Update AudioClip with new data
                    currentClip.SetData(pluginAudioBuffer, 0);
                }
                catch (Exception ex)
                {
                    DebugLogError($"Error updating AudioClip: {ex.Message}");
                }
#endif
        }

        /// <summary>
        /// Force reset all recording state
        /// </summary>
        public static void ForceReset()
        {
#if !UNITY_EDITOR
                DebugLog("Force resetting plugin state");

                try
                {
                    // Reset native plugin
                    AndroidNativeMicrophonePlugin_ForceReset();

                    // Properly destroy AudioClip if it exists
                    if (currentClip != null)
                    {
                        UnityEngine.Object.Destroy(currentClip);
                        currentClip = null;
                    }

                    // Clear managed state
                    isCurrentlyRecording = false;
                    currentDevice = null;
                    pluginAudioBuffer = null;
                    voiceProcessingEnabled = false;

                    DebugLog("Plugin and Unity state reset successfully");
                }
                catch (Exception ex)
                {
                    DebugLogError($"Error during force reset: {ex.Message}");
                }
#endif
        }

        /// <summary>
        /// Check if voice processing (echo cancellation) is currently enabled
        /// </summary>
        public static bool IsVoiceProcessingEnabled()
        {
            return voiceProcessingEnabled;
        }

        /// <summary>
        /// Request microphone permission (Android 6.0+)
        /// </summary>
        public static void RequestMicrophonePermission()
        {
#if !UNITY_EDITOR
                DebugLog("Requesting microphone permission for Android");
                
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                {
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                }
                else
                {
                    DebugLog("Microphone permission already granted");
                }
#else
            DebugLog("RequestMicrophonePermission is Android specific");
#endif
        }

        /// <summary>
        /// Check if microphone permission is granted
        /// </summary>
        public static bool HasMicrophonePermission()
        {
#if !UNITY_EDITOR
                return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#else
            return true; // Assume permission on non-Android platforms
#endif
        }

#if UNITY_EDITOR
        // Unity Editor cleanup handling
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            // Clean up when exiting play mode or edit mode
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode ||
                state == UnityEditor.PlayModeStateChange.ExitingEditMode)
            {
                try
                {
                    ForceReset();
                }
                catch (Exception ex)
                {
                    // Use warning instead of error for editor cleanup
                    Debug.LogWarning($"[AndroidNativeMicrophone] Cleanup warning: {ex.Message}");
                }
            }
        }
#endif
    }
}
#endif

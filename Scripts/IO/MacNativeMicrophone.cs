#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ChatdollKit.IO
{
    public static class MacNativeMicrophone
    {
        [DllImport("MacNativeMicrophonePlugin")]
        private static extern int MacNativeMicrophonePlugin_GetDeviceCount();
        [DllImport("MacNativeMicrophonePlugin")]
        private static extern IntPtr MacNativeMicrophonePlugin_GetDeviceName(int index);
        [DllImport("MacNativeMicrophonePlugin")]
        private static extern int MacNativeMicrophonePlugin_Start(string deviceName, int lengthSec, int frequency);
        [DllImport("MacNativeMicrophonePlugin")]
        private static extern int MacNativeMicrophonePlugin_StartWithVoiceProcessing(string deviceName, int lengthSec, int frequency, int enableVoiceProcessing);
        [DllImport("MacNativeMicrophonePlugin")]
        private static extern void MacNativeMicrophonePlugin_End();
        [DllImport("MacNativeMicrophonePlugin")]
        private static extern int MacNativeMicrophonePlugin_IsRecording();
        [DllImport("MacNativeMicrophonePlugin")]
        private static extern int MacNativeMicrophonePlugin_GetPosition();
        [DllImport("MacNativeMicrophonePlugin")]
        private static extern IntPtr MacNativeMicrophonePlugin_GetAudioData();
        [DllImport("MacNativeMicrophonePlugin")]
        private static extern void MacNativeMicrophonePlugin_ForceReset();

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
                Debug.Log($"[MacNativeMicrophone] {message}");
            }
        }

        private static void DebugLogError(string message)
        {
            Debug.LogError($"[MacNativeMicrophone] {message}");
        }

        /// <summary>
        /// Get all available audio input devices
        /// </summary>
        public static string[] devices
        {
            get
            {
                int count = MacNativeMicrophonePlugin_GetDeviceCount();
                string[] deviceNames = new string[count];

                for (int i = 0; i < count; i++)
                {
                    IntPtr namePtr = MacNativeMicrophonePlugin_GetDeviceName(i);
                    deviceNames[i] = Marshal.PtrToStringAnsi(namePtr);
                }

                return deviceNames;
            }
        }

        /// <summary>
        /// Start recording with default voice processing enabled
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
        /// <param name="frequency">Sample rate in Hz</param>
        /// <param name="enableVoiceProcessing">Enable macOS voice processing (echo cancellation, noise reduction)</param>
        public static AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency, bool enableVoiceProcessing)
        {
            // Force cleanup if already recording
            if (isCurrentlyRecording)
            {
                ForceReset();
            }

            string targetDevice = string.IsNullOrEmpty(deviceName) ? "Default" : deviceName;

            DebugLog($"Starting recording: device={targetDevice}, length={lengthSec}s, freq={frequency}Hz, voiceProcessing={enableVoiceProcessing}");

            // Call appropriate native function based on voice processing setting
            int result = enableVoiceProcessing
                ? MacNativeMicrophonePlugin_StartWithVoiceProcessing(targetDevice, lengthSec, frequency, 1)
                : MacNativeMicrophonePlugin_Start(targetDevice, lengthSec, frequency);

            if (result == 1)
            {
                // Create Unity AudioClip as container
                currentClip = AudioClip.Create("MicrophoneClip", lengthSec * frequency, 1, frequency, false);
                
                // IMPORTANT: Set HideFlags to prevent serialization issues in Editor
#if UNITY_EDITOR
                currentClip.hideFlags = HideFlags.DontSave;
#endif
                
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
        }

        /// <summary>
        /// Stop recording on specified device
        /// </summary>
        public static void End(string deviceName)
        {
            if (!isCurrentlyRecording)
                return;

            // Check if we're ending the correct device
            if (!string.IsNullOrEmpty(deviceName) && deviceName != currentDevice)
                return;

            DebugLog("Ending recording");
            MacNativeMicrophonePlugin_End();

            // Properly destroy AudioClip
            if (currentClip != null)
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(currentClip);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(currentClip);
                }
#else
                UnityEngine.Object.Destroy(currentClip);
#endif
                currentClip = null;
            }

            // Clear state
            isCurrentlyRecording = false;
            currentDevice = null;
            pluginAudioBuffer = null;
            voiceProcessingEnabled = false;
        }

        /// <summary>
        /// Check if currently recording on specified device
        /// </summary>
        public static bool IsRecording(string deviceName)
        {
            if (!isCurrentlyRecording)
                return false;

            // Check device match
            if (!string.IsNullOrEmpty(deviceName) && deviceName != currentDevice)
                return false;

            // Verify with native plugin
            bool pluginIsRecording = MacNativeMicrophonePlugin_IsRecording() == 1;

            if (!pluginIsRecording)
            {
                isCurrentlyRecording = false;
                return false;
            }

            // Update AudioClip with latest data
            UpdateAudioClip();
            return true;
        }

        /// <summary>
        /// Get current recording position in samples
        /// </summary>
        public static int GetPosition(string deviceName)
        {
            if (!IsRecording(deviceName))
                return 0;

            return MacNativeMicrophonePlugin_GetPosition();
        }

        /// <summary>
        /// Transfer audio data from native plugin to Unity AudioClip
        /// </summary>
        private static void UpdateAudioClip()
        {
            if (currentClip == null || pluginAudioBuffer == null)
                return;

            IntPtr dataPtr = MacNativeMicrophonePlugin_GetAudioData();
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
        }

        /// <summary>
        /// Force reset all recording state (useful for Unity Editor)
        /// </summary>
        public static void ForceReset()
        {
            DebugLog("Force resetting plugin state");

            try
            {
                // Reset native plugin
                MacNativeMicrophonePlugin_ForceReset();

                // Properly destroy AudioClip if it exists
                if (currentClip != null)
                {
#if UNITY_EDITOR
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(currentClip);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(currentClip);
                    }
#else
                    UnityEngine.Object.Destroy(currentClip);
#endif
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
        }

        /// <summary>
        /// Check if voice processing is currently enabled
        /// </summary>
        public static bool IsVoiceProcessingEnabled()
        {
            return voiceProcessingEnabled;
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
                    Debug.LogWarning($"[MacNativeMicrophone] Cleanup warning: {ex.Message}");
                }
            }
        }
#endif
    }
}
#endif

#if UNITY_ANDROID

using UnityEngine;
using ChatdollKit.IO;

namespace ChatdollKit.SpeechListener
{
    public class AndroidMicrophoneProvider : IMicrophoneProvider
    {
        public bool IsRecording(string deviceName) => AndroidNativeMicrophone.IsRecording(deviceName);
        public AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency)
            => AndroidNativeMicrophone.Start(deviceName, loop, lengthSec, frequency);
        public void End(string deviceName) => AndroidNativeMicrophone.End(deviceName);
        public int GetPosition(string deviceName) => AndroidNativeMicrophone.GetPosition(deviceName);
        public string[] devices => AndroidNativeMicrophone.devices;
    }
}

#endif

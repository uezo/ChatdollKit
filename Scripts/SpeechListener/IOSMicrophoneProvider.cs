#if UNITY_IOS

using UnityEngine;
using ChatdollKit.IO;

namespace ChatdollKit.SpeechListener
{
    public class IOSMicrophoneProvider : IMicrophoneProvider
    {
        public bool IsRecording(string deviceName) => IOSNativeMicrophone.IsRecording(deviceName);
        public AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency)
            => IOSNativeMicrophone.Start(deviceName, loop, lengthSec, frequency);
        public void End(string deviceName) => IOSNativeMicrophone.End(deviceName);
        public int GetPosition(string deviceName) => IOSNativeMicrophone.GetPosition(deviceName);
        public string[] devices => IOSNativeMicrophone.devices;
    }
}

#endif

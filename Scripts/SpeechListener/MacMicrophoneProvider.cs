#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX

using UnityEngine;
using ChatdollKit.IO;

namespace ChatdollKit.SpeechListener
{
    public class MacMicrophoneProvider : IMicrophoneProvider
    {
        public bool IsRecording(string deviceName) => MacNativeMicrophone.IsRecording(deviceName);
        public AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency)
            => MacNativeMicrophone.Start(deviceName, loop, lengthSec, frequency);
        public void End(string deviceName) => MacNativeMicrophone.End(deviceName);
        public int GetPosition(string deviceName) => MacNativeMicrophone.GetPosition(deviceName);
        public string[] devices => MacNativeMicrophone.devices;
    }
}

#endif

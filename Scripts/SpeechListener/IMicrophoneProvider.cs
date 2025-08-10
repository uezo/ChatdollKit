using UnityEngine;

namespace ChatdollKit.SpeechListener
{
    public interface IMicrophoneProvider
    {
        bool IsRecording(string deviceName);
        AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency);
        void End(string deviceName);
        int GetPosition(string deviceName);
        string[] devices { get; }
    }
}

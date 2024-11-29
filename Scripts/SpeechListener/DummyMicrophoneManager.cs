using UnityEngine;

namespace ChatdollKit.SpeechListener
{
    // Dummy microphone for non-voice conversation use case (e.g. AITuber)
    public class DummyMicrophoneManager : MonoBehaviour, IMicrophoneManager
    {
        public void MuteMicrophone(bool mute)
        {
        }

        public void SetNoiseGateThresholdDb(float db)
        {
        }

        public void StartMicrophone()
        {
        }

        public void StopMicrophone()
        {
        }
    }
}

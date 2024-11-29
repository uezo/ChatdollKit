namespace ChatdollKit.SpeechListener
{
    public interface IMicrophoneManager
    {
        void StartMicrophone();
        void StopMicrophone();
        void MuteMicrophone(bool mute);
        void SetNoiseGateThresholdDb(float db);
    }
}

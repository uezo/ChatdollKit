using System;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechListener
{
    public interface ISpeechListener
    {
        Func<string, UniTask> OnRecognized { get; set; }
        bool IsRecording{ get; }
        void StartListening(bool stopBeforeStart = false);
        void StopListening();
        void ChangeSessionConfig(float silenceDurationThreshold = float.MinValue, float minRecordingDuration = float.MinValue, float maxRecordingDuration = float.MinValue);
    }
}

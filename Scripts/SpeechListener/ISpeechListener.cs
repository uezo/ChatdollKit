using System;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechListener
{
    public interface ISpeechListener
    {
        bool IsEnabled { get; set; }
        Func<string, UniTask> OnRecognized { get; set; }
        Action OnBargeIn { get; set; }
        Func<string, float, bool> BargeInCondition { get; set; }
        bool IsRecording{ get; }
        bool IsVoiceDetected { get; }
        void StartListening(bool stopBeforeStart = false);
        void StopListening();
        void ChangeSessionConfig(float silenceDurationThreshold = float.MinValue, float minRecordingDuration = float.MinValue, float maxRecordingDuration = float.MinValue);
    }
}

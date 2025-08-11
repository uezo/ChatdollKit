using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechListener
{
    // Dummy speech listener for non-voice conversation use case (e.g. AITuber)
    public class DummySpeechListener : MonoBehaviour, ISpeechListener
    {
        public Func<string, UniTask> OnRecognized { get; set; }
        public bool IsRecording { get; }
        public void ChangeSessionConfig(float silenceDurationThreshold = float.MinValue, float minRecordingDuration = float.MinValue, float maxRecordingDuration = float.MinValue)
        {
        }
        public void StartListening(bool stopBeforeStart = false)
        {
        }
        public void StopListening()
        {
        }
    }
}

using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.STT;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.VAD
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/VAD/Standard Speech Detector")]
    public sealed class StandardSpeechDetector : SpeechDetectorComponent
    {
        public SpeechDetectorSettings Settings = new SpeechDetectorSettings();
        public float VolumeDbThreshold = -40;

        public override SpeechDetectorOptions BuildOptions()
        {
            var options = new StandardSpeechDetectorOptions { VolumeDbThreshold = VolumeDbThreshold };
            Settings.ApplyTo(options);
            options.Validate();
            return options;
        }

        public override UniTask<SpeechDetectorLease> CreateDetectorAsync(ISpeechRecognizer stt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(new SpeechDetectorLease(new StandardSpeechDetectorEngine((StandardSpeechDetectorOptions)BuildOptions())));
        }
    }
}

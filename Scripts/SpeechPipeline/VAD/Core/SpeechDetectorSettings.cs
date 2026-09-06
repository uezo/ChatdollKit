using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.VAD
{
    [Serializable]
    public sealed class SpeechDetectorSettings
    {
        public int SampleRate = 16000;
        public float SilenceDurationThreshold = 0.5f;
        public float MinDuration = 0.2f;
        public float MaxDuration = 10f;
        public int PrerollBufferCount = 5;
        public float RecordingStartedMinDuration = 1.5f;
        public int RecordingStartedMinTextLength = 2;

        public void ApplyTo(SpeechDetectorOptions options)
        {
            options.SampleRate = SampleRate;
            options.Channels = 1;
            options.SilenceDurationThreshold = SilenceDurationThreshold;
            options.MinDuration = MinDuration;
            options.MaxDuration = MaxDuration;
            options.PrerollBufferCount = PrerollBufferCount;
            options.RecordingStartedMinDuration = RecordingStartedMinDuration;
            options.RecordingStartedMinTextLength = RecordingStartedMinTextLength;
        }
    }
}

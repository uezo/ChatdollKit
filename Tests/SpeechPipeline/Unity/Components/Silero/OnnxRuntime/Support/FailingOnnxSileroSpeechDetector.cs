using System;
using ChatdollKit.Extension.SileroOnnxRuntime;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;
using ChatdollKit.SpeechPipeline.VAD.Silero;
using UnityEngine;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Silero.OnnxRuntime
{
    [AddComponentMenu("")]
    public sealed class FailingOnnxSileroSpeechDetector : OnnxSileroSpeechDetector
    {
        public const string FailureMessage = "Injected detector construction failure.";
        public ISileroVadModel CapturedModel { get; private set; }

        protected override ISpeechDetector CreateDetector(ISileroVadModel model, ISpeechRecognizer stt, SileroSpeechDetectorOptions options)
        {
            CapturedModel = model;
            throw new InvalidOperationException(FailureMessage);
        }
    }
}

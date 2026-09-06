using System;
using System.Collections.Generic;
using System.Threading;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline.STT;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Integration
{
    public sealed class OfflineRecognizerComponent : SpeechRecognizerComponent
    {
        public readonly List<OfflineRecognizer> Created = new List<OfflineRecognizer>();
        public override SpeechRecognizerOptions BuildOptions(SpeechRecognizerOptions current = null)
            => current?.Copy() ?? new SpeechRecognizerOptions();
        public override ISpeechRecognizer CreateRecognizer(SpeechRecognizerOptions options)
        {
            var result = new OfflineRecognizer(options); Created.Add(result); return result;
        }
    }
}

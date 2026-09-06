using System;
using System.Collections.Generic;
using System.Threading;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline.TTS;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Integration
{
    public sealed class OfflineSynthesizerComponent : SpeechSynthesizerComponent
    {
        public readonly List<OfflineSynthesizer> Created = new List<OfflineSynthesizer>();
        public override SpeechSynthesizerOptions BuildOptions(SpeechSynthesizerOptions current = null)
            => current?.Copy() ?? new SpeechSynthesizerOptions();
        public override ISpeechSynthesizer CreateSynthesizer(SpeechSynthesizerOptions options)
        {
            var result = new OfflineSynthesizer(options); Created.Add(result); return result;
        }
    }
}

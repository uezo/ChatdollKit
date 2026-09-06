using System;
using System.Collections.Generic;
using System.Threading;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline.LLM;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Integration
{
    public sealed class OfflineLlmComponent : LlmServiceComponent
    {
        public readonly List<OfflineLlm> Created = new List<OfflineLlm>();
        public string Model = "offline-one";
        public override LlmHistoryFormat? HistoryFormat => LlmHistoryFormat.ChatCompletions;
        public override LlmServiceOptions BuildOptions(LlmServiceOptions current = null)
        {
            var options = current?.Copy() ?? new LlmServiceOptions { ApiKey = "unused-offline-test-key" };
            options.Model = Model;
            return options;
        }
        public override ILlmService CreateService(LlmServiceOptions options)
        {
            var result = new OfflineLlm(options); Created.Add(result); return result;
        }
    }
}

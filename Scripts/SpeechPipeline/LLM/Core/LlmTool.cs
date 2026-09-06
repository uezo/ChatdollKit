using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>A function definition and optional application-owned implementation. No tool integrations are bundled.</summary>
    public sealed class LlmTool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JObject Parameters { get; set; } = new JObject { ["type"] = "object", ["properties"] = new JObject() };
        public bool? Strict { get; set; }
        public Func<LlmToolCall, LlmRequest, CancellationToken, UniTask<LlmToolResult>> ExecuteAsync { get; set; }
        public LlmTool Copy() => new LlmTool
        {
            Name = Name, Description = Description, Parameters = (JObject)Parameters?.DeepClone(),
            Strict = Strict, ExecuteAsync = ExecuteAsync
        };
    }

    public sealed class LlmToolResult
    {
        public JToken Data { get; set; }
        /// <summary>Optional text to emit immediately while handling the tool.</summary>
        public string Text { get; set; }
        public JObject StructuredContent { get; set; }
        public bool ContinueChain { get; set; } = true;
    }
}

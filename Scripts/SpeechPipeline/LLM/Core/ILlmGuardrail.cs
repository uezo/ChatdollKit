using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public enum LlmGuardrailScope { Request, Response, Both }
    public enum LlmGuardrailAction { Replace, Block }

    public interface ILlmGuardrail
    {
        LlmGuardrailScope Scope { get; }
        UniTask<LlmGuardrailResult> ApplyAsync(LlmRequest request, string text, CancellationToken cancellationToken);
    }

    public sealed class LlmGuardrailResult
    {
        public bool IsTriggered { get; set; }
        public string Name { get; set; }
        public LlmGuardrailAction Action { get; set; } = LlmGuardrailAction.Replace;
        public string Text { get; set; }
    }
}

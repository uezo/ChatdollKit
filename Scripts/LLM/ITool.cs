using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM
{
    public interface ITool
    {
        ILLMTool GetToolSpec();
        UniTask<ILLMSession> ProcessAsync(ILLMService llmService, ILLMSession llmSession, Dictionary<string, object> payloads, CancellationToken token);
    }
}

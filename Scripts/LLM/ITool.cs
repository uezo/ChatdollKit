using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM
{
    public interface ITool
    {
        ILLMTool GetToolSpec();
        UniTask<ILLMSession> ProcessAsync(ILLMService llmService, ILLMSession llmSession, Dictionary<string, object> payloads, CancellationToken token);
        UniTask<ToolResponse> ExecuteAsync(string argumentsJsonString, CancellationToken token);
    }

    public class ToolResponse
    {
        public string Body { get; protected set; }
        public string Role { get; protected set; }

        public ToolResponse(string body, string role = "function")
        {
            Body = body;
            Role = role;
        }
    }
}

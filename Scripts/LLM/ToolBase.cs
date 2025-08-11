using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ChatdollKit.LLM
{
    public class ToolBase : MonoBehaviour, ITool
    {
        public virtual ILLMTool GetToolSpec()
        {
            throw new NotImplementedException("ToolBase.GetToolSpec must be implemented");
        }

        public async UniTask<ILLMSession> ProcessAsync(ILLMService llmService, ILLMSession llmSession, Dictionary<string, object> payloads, CancellationToken token)
        {
            // Implementation for migration from older version
            return null;
        }

        public virtual async UniTask<ToolResponse> ExecuteAsync(string argumentsJsonString, CancellationToken token)
        {
            // Implementation for migration from older version
            return await ExecuteFunction(argumentsJsonString, token);
        }

#pragma warning disable CS1998
        protected virtual async UniTask<ToolResponse> ExecuteFunction(string argumentsJsonString, CancellationToken token)
        {
            throw new NotImplementedException("ToolBase.ExecuteFunction must be implemented");
        }
#pragma warning restore CS1998
    }
}

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
            // TODO: Waiting AnimatedVoice. See https://x.com/uezochan/status/1795216169969864865
            await llmSession.StreamingTask;

            // Execute function
            var responseForRequest = await ExecuteFunction(llmSession.StreamBuffer, token);

            // Add human message for next request
            var humanFriendlyAnswerRequestMessage = llmService.CreateMessageAfterFunction(responseForRequest.Role, responseForRequest.Body, llmSession: llmSession);
            llmSession.Contexts.Add(humanFriendlyAnswerRequestMessage);

            // Call LLM to get human-friendly response
            var llmSessionForHuman = await llmService.GenerateContentAsync(llmSession.Contexts, payloads, false, token: token);
            llmSessionForHuman.OnStreamingEnd = llmSession.OnStreamingEnd;

            return llmSessionForHuman;
        }

#pragma warning disable CS1998
        protected virtual async UniTask<ToolResponse> ExecuteFunction(string argumentsJsonString, CancellationToken token)
        {
            throw new NotImplementedException("ToolBase.ExecuteFunction must be implemented");
        }
#pragma warning restore CS1998

        protected class ToolResponse
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
}

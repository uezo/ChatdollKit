using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.LLM
{
    public class LLMFunctionSkillBase : LLMContentSkill
    {
        public virtual ILLMTool GetToolSpec()
        {
            throw new NotImplementedException("LLMFunctionSkillBase.GetToolSpec must be implemented");
        }

        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            var llmSession = (ILLMSession)request.Payloads["LLMSession"];

            // TODO: Waiting AnimatedVoice. See https://x.com/uezochan/status/1795216169969864865
            await llmSession.StreamingTask;

            // Execute function
            var responseForRequest = await ExecuteFunction(llmSession.StreamBuffer, request, state, user, token);

            // Add human message for next request
            var humanFriendlyAnswerRequestMessage = llmService.CreateMessageAfterFunction(responseForRequest.Role, responseForRequest.Body, llmSession: llmSession);
            llmSession.Contexts.Add(humanFriendlyAnswerRequestMessage);

            // Call LLM to get human-friendly response
            var payloads = new Dictionary<string, object>();
            payloads.Add("RequestPayloads", request.Payloads);
            payloads.Add("StateData", state.Data);
            var llmSessionForHuman = await llmService.GenerateContentAsync(llmSession.Contexts, payloads, false, token: token);

            // Make response
            var response = new Response(request.Id, endTopic: false);
            response.Payloads = new Dictionary<string, object>()
            {
                { "LLMSession", llmSessionForHuman }
            };

            return response;
        }

#pragma warning disable CS1998
        protected virtual async UniTask<FunctionResponse> ExecuteFunction(string argumentsJsonString, Request request, State state, User user, CancellationToken token)
        {
            throw new NotImplementedException("LLMFunctionSkillBase.ExecuteFunction must be implemented");
        }
#pragma warning restore CS1998

        protected class FunctionResponse
        {
            public string Body { get; protected set; }
            public string Role { get; protected set; }

            public FunctionResponse(string body, string role = "function")
            {
                Body = body;
                Role = role;
            }
        }
    }
}

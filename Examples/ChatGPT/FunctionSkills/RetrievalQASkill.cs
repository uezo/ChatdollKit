using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Network;
using ChatdollKit.LLM;

namespace ChatdollKit.Examples.ChatGPT
{
    public class RetrievalQASkill : LLMFunctionSkillBase
    {
        public string FunctionName = "get_openai_terms_of_use";
        public string FunctionDescription = "Get information about terms of use of OpenAI services including ChatGPT.";
        protected ChatdollHttp client = new ChatdollHttp(debugFunc: Debug.LogWarning);
        [SerializeField]
        [TextArea(1, 6)]
        protected string questionPrompt = "Please respond to user questions based on the following information. Keep the response within 200 characters and make it suitable for direct reading aloud.\n\n";

        public override ILLMTool GetToolSpec()
        {
            // Make function spec for ChatGPT Function Calling
            return new LLMTool(FunctionName, FunctionDescription);
        }

        protected override async UniTask<FunctionResponse> ExecuteFunction(string argumentsJsonString, Request request, State state, User user, CancellationToken token)
        {
            var parameters = new Dictionary<string, string>()
            {
                { "q",  request.Text}
            };

            // Get info for grounding. Use VSSLite to make vector search engine extremely easy https://github.com/uezo/vsslite
            var vssResponse = await client.GetJsonAsync<VectorSearchResponse>(
                "http://127.0.0.1:8000/search/openai", parameters: parameters
            );

            // Make prompt
            var prompt = questionPrompt;
            foreach (var doc in vssResponse.results)
            {
                prompt += $"----------------\n{doc.page_content}\n\n";
            }

            return new FunctionResponse(prompt, "user");
        }

        protected class Document
        {
            public string page_content { get; set; }
        }

        protected class VectorSearchResponse
        {
            public List<Document> results;
        }
    }
}

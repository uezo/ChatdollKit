using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.LLM;

namespace ChatdollKit.Extension.ChatMemory
{
    public class ChatMemoryTool : ToolBase
    {
        public bool IncludeRetrivedData;
        private ChatMemoryIntegrator chatMemoryIntegrator;

        public string FunctionName = "search_memory";
        public string FunctionDescription = "Search and retrieve long-term memory.";

        private void Start()
        {
            chatMemoryIntegrator = gameObject.GetComponent<ChatMemoryIntegrator>();
        }

        public override ILLMTool GetToolSpec()
        {
            // Make function spec for Function Calling
            var func = new LLMTool(FunctionName, FunctionDescription);
            func.AddProperty("query", new Dictionary<string, object>() { { "type", "string" } });
            return func;
        }

        protected override async UniTask<ToolResponse> ExecuteFunction(string argumentsJsonString, CancellationToken token)
        {
            var arguments = JsonConvert.DeserializeObject<Dictionary<string, string>>(argumentsJsonString);
            var searchResponse = await chatMemoryIntegrator.SearchMemory(arguments["query"], include_retrieved_data: IncludeRetrivedData);
            return new ToolResponse(JsonConvert.SerializeObject(searchResponse.result));
        }
    }
}

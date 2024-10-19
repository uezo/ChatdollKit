using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.LLM;

namespace ChatdollKit.Examples.ChatGPT
{
    public class WeatherTool : ToolBase
    {
        public string FunctionName = "get_weather";
        public string FunctionDescription = "Get current weather in the location.";

        public override ILLMTool GetToolSpec()
        {
            // Make function spec for Function Calling
            var func = new LLMTool(FunctionName, FunctionDescription);
            func.AddProperty("location", new Dictionary<string, object>() { { "type", "string" } });
            return func;
        }

        protected override async UniTask<ToolResponse> ExecuteFunction(string argumentsJsonString, CancellationToken token)
        {
            // Parse arguments
            var arguments = JsonConvert.DeserializeObject<Dictionary<string, string>>(argumentsJsonString);

            Debug.Log($"location: {arguments["location"]}");

            // Call API (new Dict instead)
            var resp = new Dictionary<string, object>()
            {
                { "weather", "Fine" },
                { "temperature", 23.5 }
            };
            await UniTask.Delay(100);

            // Return response as serialized JSON
            return new ToolResponse(JsonConvert.SerializeObject(resp));
        }
    }
}

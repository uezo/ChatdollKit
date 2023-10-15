using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.Examples.ChatGPT
{
    public class WeatherSkill : ChatGPTFunctionSkillBase
    {
        public string FunctionName = "get_weather";
        public string FunctionDescription = "Get current weather in the location.";

        public override ChatGPTFunction GetFunctionSpec()
        {
            // Make function spec for ChatGPT Function Calling
            var func = new ChatGPTFunction(FunctionName, FunctionDescription);
            func.AddProperty("location", new Dictionary<string, object>() { { "type", "string" } });
            return func;
        }

        protected override async UniTask<FunctionResponse> ExecuteFunction(string argumentsJsonString, Request request, State state, User user, CancellationToken token)
        {
            // Parse arguments
            var arguments = JsonConvert.DeserializeObject<Dictionary<string, string>>(argumentsJsonString);

            Debug.Log($"location: {arguments["location"]}");

            // Call API (new Dict instead)
            var resp = new Dictionary<string, object>()
            {
                { "weather", "Fine" },
                { "temperature", 36.7 }
            };

            // Return response as serialized JSON
            return new FunctionResponse(JsonConvert.SerializeObject(resp));
        }
    }
}

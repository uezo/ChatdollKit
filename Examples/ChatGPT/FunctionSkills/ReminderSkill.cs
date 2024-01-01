using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.LLM;

namespace ChatdollKit.Examples.ChatGPT
{
    public class ReminderSkill : LLMFunctionSkillBase
    {
        public string FunctionName = "add_reminder";
        public string FunctionDescription = "Add item to reminder.";

        public override ILLMTool GetToolSpec()
        {
            // Make function spec for ChatGPT Function Calling
            var func = new LLMTool(FunctionName, FunctionDescription);
            func.AddProperty("title", new Dictionary<string, object>() { { "type", "string" } });
            func.AddProperty("remind_at", new Dictionary<string, object>() { { "type", "string" }, { "format", "date-time" } });
            return func;
        }

        protected override async UniTask<FunctionResponse> ExecuteFunction(string argumentsJsonString, Request request, State state, User user, CancellationToken token)
        {
            // Parse arguments
            var arguments = JsonConvert.DeserializeObject<Dictionary<string, string>>(argumentsJsonString);

            Debug.Log($"title: {arguments["title"]} / remind_at: {arguments["remind_at"]}");

            // Call API (new Dict instead)
            var resp = new Dictionary<string, object>()
            {
                { "title", arguments["title"] },
                { "temperature", arguments["remind_at"] }
            };
            await UniTask.Delay(100);

            // Return response as serialized JSON
            return new FunctionResponse(JsonConvert.SerializeObject(resp));
        }
    }
}

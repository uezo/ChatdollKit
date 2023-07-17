using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.Examples.ChatGPT
{
    public class ReminderSkill : ChatGPTFunctionSkillBase
    {
        public string FunctionName = "add_reminder";
        public string FunctionDescription = "Add item to reminder.";

        public override ChatGPTFunction GetFunctionSpec()
        {
            // Make function spec for ChatGPT Function Calling
            var func = new ChatGPTFunction(FunctionName, FunctionDescription);
            func.AddProperty("title", new Dictionary<string, object>() { { "type", "string" } });
            func.AddProperty("remind_at", new Dictionary<string, object>() { { "type", "string" }, { "format", "date-time" } });
            return func;
        }

        protected override async UniTask<string> ExecuteFunction(string argumentsJsonString, CancellationToken token)
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

            // Return response as serialized JSON
            return JsonConvert.SerializeObject(resp);
        }
    }
}

using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.LLM
{
    public class LLMRouter : SkillRouterBase
    {
        protected ILLMService llmService { get; set; }
        protected Dictionary<string, string> toolResolver { get; set; }

        [SerializeField]
        protected string contentSkillName = "LLMContent";

        public override List<ISkill> RegisterSkills()
        {
            // Get enabled LLMService
            foreach (var s in GetComponents<ILLMService>())
            {
                if (s.IsEnabled)
                {
                    llmService = s;
                    break;
                }
            }

            var llmFunctionSkills = new List<ISkill>();
            toolResolver = new Dictionary<string, string>();

            // Register skills and get tool spec
            foreach (var skill in base.RegisterSkills())
            {
                if (skill is LLMFunctionSkillBase)
                {
                    llmFunctionSkills.Add(skill);
                    var tool = ((LLMFunctionSkillBase)skill).GetToolSpec();
                    llmService.AddTool(tool);
                    toolResolver.Add(tool.name, skill.TopicName);
                }
            }

            return llmFunctionSkills;
        }

        public override async UniTask<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            var payloads = new Dictionary<string, object>();
            payloads.Add("RequestPayloads", request.Payloads);
            payloads.Add("StateData", state.Data);

            var messages = await llmService.MakePromptAsync(state.UserId, request.Text, payloads, token);

            var llmSession = await llmService.GenerateContentAsync(messages, payloads, token: token);
            request.Payloads.Add("LLMSession", llmSession);

            if (!string.IsNullOrEmpty(llmSession.FunctionName))
            {
                if (toolResolver.ContainsKey(llmSession.FunctionName))
                {
                    return new IntentExtractionResult(new Intent(toolResolver[llmSession.FunctionName], Priority.Highest, true));
                }
            }

            return new IntentExtractionResult(contentSkillName.ToLower(), Priority.Normal);
        }
    }
}

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

        public virtual void Initialize()
        {
            // Get LLMServices and configure
            foreach (var llm in GetComponents<ILLMService>())
            {
                // Set LLMService to use
                if (llm.IsEnabled && llmService == null)
                {
                    llmService = llm;
                    Debug.Log($"Use LLMService: {llmService}");
                }

                // Add OnChangeAction
                llm.OnEnabled = () =>
                {
                    SetLLMService(llm);
                };
            }

            if (llmService == null)
            {
                llmService = GetComponent<ILLMService>();

                if (llmService == null)
                {
                    Debug.LogWarning("No LLMServices are available.");
                }
                else
                {
                    Debug.LogWarning($"Enabled LLMServices not found. Use {llmService}.");
                    llmService.IsEnabled = true;
                }
            }
        }

        public virtual void SetLLMService(ILLMService llmService)
        {
            // Use this LLMService
            this.llmService = llmService;

            // Set IsEnabled=false to other LLMServices
            foreach (var s in GetComponents<ILLMService>())
            {
                if (s != llmService)
                {
                    s.IsEnabled = false;
                }
            }

            // Set LLMService to skills
            foreach (var skill in topicResolver.Values)
            {
                if (skill is LLMContentSkill)
                {
                    ((LLMContentSkill)skill).SetLLMService(this.llmService);
                }
            }
        }

        public override List<ISkill> RegisterSkills()
        {
            if (llmService == null)
            {
                Initialize();
            }

            // Register tool spec to toolResolver
            var llmFunctionSkills = new List<ISkill>();
            toolResolver = new Dictionary<string, string>();
            foreach (var skill in base.RegisterSkills())
            {
                if (skill is not LLMFunctionSkillBase) continue;

                // Set tool to resolver
                var tool = ((LLMFunctionSkillBase)skill).GetToolSpec();
                toolResolver.Add(tool.name, skill.TopicName);

                // Set skill as tool to LLMServices
                foreach (var llm in GetComponents<ILLMService>())
                {
                    llm.AddTool(tool);
                }

                llmFunctionSkills.Add(skill);
            }

            // Set LLMService to skills
            SetLLMService(llmService);

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

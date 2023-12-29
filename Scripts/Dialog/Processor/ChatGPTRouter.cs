using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog.Processor
{
    public class ChatGPTRouter : SkillRouterBase
    {
        private ChatGPTService chatGPT;
        private Dictionary<string, string> functionResolver;

        public override List<ISkill> RegisterSkills()
        {
            // Get ChatGPT service for supported platform
#if UNITY_WEBGL && !UNITY_EDITOR
            chatGPT = GetComponent<ChatGPTServiceWebGL>();
            if (chatGPT == null)
            {
                UnityEngine.Debug.LogWarning("ChatGPTService doesn't support stream. Use ChatGPTServiceWebGL instead.");
                chatGPT = GetComponent<ChatGPTService>();
            }
#else
            foreach (var gpt in GetComponents<ChatGPTService>())
            {
                if (gpt is not ChatGPTServiceWebGL)
                {
                    chatGPT = gpt;
                    break;
                }
            }
#endif

            var functionSkills = new List<ISkill>();
            functionResolver = new Dictionary<string, string>();

            // Register skills and get tool spec
            foreach (var skill in base.RegisterSkills())
            {
                if (skill is ChatGPTFunctionSkillBase)
                {
                    functionSkills.Add(skill);
                    var func = ((ChatGPTFunctionSkillBase)skill).GetFunctionSpec();
                    chatGPT.AddFunction(func);
                    functionResolver.Add(func.name, skill.TopicName);
                }
            }

            return functionSkills;
        }

        public override async UniTask<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            var messages = new List<ChatGPTMessage>();

            // System
            if (!string.IsNullOrEmpty(chatGPT.SystemMessageContent))
            {
                messages.Add(new ChatGPTMessage("system", chatGPT.SystemMessageContent));
            }

            // Histories
            messages.AddRange(chatGPT.GetHistories(state));

            // User (current input)
            messages.Add(new ChatGPTMessage("user", request.Text));

            var chatGPTSession = await chatGPT.GenerateContentAsync(messages, token: token);
            request.Payloads.Add("LLMSession", chatGPTSession);

            if (!string.IsNullOrEmpty(chatGPTSession.FunctionName))
            {
                if (functionResolver.ContainsKey(chatGPTSession.FunctionName))
                {
                    return new IntentExtractionResult(new Intent(functionResolver[chatGPTSession.FunctionName], Priority.Highest, true));
                }
            }

            return new IntentExtractionResult("chatgptcontent", Priority.Normal);
        }
    }
}

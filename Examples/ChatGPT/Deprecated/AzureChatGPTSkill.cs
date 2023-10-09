using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Network;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace ChatdollKit.Examples.ChatGPT
{
    public class AzureChatGPTSkill : SkillBase
    {
        [Header("API configuration")]
        [SerializeField]
        private string apiKey;
        [SerializeField]
        private string resourceName;
        [SerializeField]
        private string deploymentName;
        [SerializeField]
        private string apiVersion = "2022-12-01";
        [SerializeField]
        private int maxTokens = 2000;
        [SerializeField]
        private float temperature = 0.5f;

        [Header("Context configuration")]
        [SerializeField]
        private string userRole = "Human";
        [SerializeField]
        private string assistantRole = "AI";
        [SerializeField]
        private string chatCondition;
        [SerializeField]
        private int historyTurns = 10;

        private List<Dictionary<string, string>> histories = new List<Dictionary<string, string>>();
        private ChatdollHttp client = new ChatdollHttp(timeout: 20000);

        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            if (state.IsNew)
            {
                // Clear history when the state is newly created
                histories.Clear();
            }

            // Auth header
            var headers = new Dictionary<string, string>()
            {
                { "api-key", apiKey },
                { "Content-Type", "application/json" }
            };

            var messages = new List<Dictionary<string, string>>();

            // Condition
            messages.Add(new Dictionary<string, string>() {
                { "role", "system" },
                { "content", $"[Roles]\nuser: {userRole}\nassistant: {assistantRole}\n\n[Condition]\n{chatCondition}" }
            });

            // Histories
            messages.AddRange(histories.Skip(histories.Count - historyTurns * 2).ToList());

            // Input
            messages.Add(new Dictionary<string, string>() {
                { "role", "user" },
                { "content", request.Text }
            });

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "prompt", CreatePrompt(messages) },
                { "max_tokens", maxTokens },
                { "temperature", temperature },
                { "stop", new List<string>() { "<|im_end|>" } }
            };

            // Make request URL
            var url = $"https://{resourceName}.openai.azure.com/openai/deployments/{deploymentName}/completions?api-version={apiVersion}";

            // Call API
            Debug.Log("data: " + string.Join(", ", data.Select(x => x.Key + "=" + x.Value).ToArray()));
            Debug.Log("url: " + url);
            var chatHttpResponse = await client.PostJsonAsync<AzureChatGPTResponse>(url, data, headers, cancellationToken: token);
            var responseText = chatHttpResponse.choices[0].text.Trim();

            // Make chat response
            var response = new Response(request.Id);
            var responseTextToSplit = responseText.Replace("。", "。|").Replace("！", "！|").Replace("？", "？|");

            foreach (var text in responseTextToSplit.Split('|'))
            {
                if (!string.IsNullOrEmpty(text.Trim()))
                {
                    response.AddVoiceTTS(text);
                    Debug.Log(text);
                }
            }

            // Update histories
            histories.Add(messages.Last());
            histories.Add(new Dictionary<string, string>() {
                { "role", "assistant" },
                { "content", responseText }
            });

            return response;
        }

        public class AzureChatGPTResponse
        {
            public string id { get; set; }
            public List<Choice> choices { get; set; }
        }

        public class Choice
        {
            // public Dictionary<string, string> message { get; set; }
            public string text { get; set; }
        }

        public string CreatePrompt(List<Dictionary<string, string>> messages)
        {
            var prompt = "";
            foreach (var message in messages)
            {
                prompt += FormatMessageToChatML(message);
            }
            prompt += "\n<|im_start|>assistant\n";
            return prompt;
        }

        public static string FormatMessageToChatML(Dictionary<string, string> message)
        {
            return $"\n<|im_start|>{message["role"]}\n{message["content"]}\n<|im_end|>";
        }
    }

}
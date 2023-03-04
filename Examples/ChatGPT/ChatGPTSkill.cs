using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Network;

namespace ChatdollKit.Examples.ChatGPT
{
    public class ChatGPTSkill : SkillBase
    {
        [Header("API configuration")]
        [SerializeField]
        private string apiKey;
        [SerializeField]
        private string model = "gpt-3.5-turbo";
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
                { "Authorization", "Bearer " + apiKey }
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

            // Make requesta data
            var data = new Dictionary<string, object>()
            {
                { "model", model },
                { "max_tokens", maxTokens },
                { "temperature", temperature },
                { "messages", messages }
            };

            // Call API
            var chatHttpResponse = await client.PostJsonAsync<ChatGPTResponse>("https://api.openai.com/v1/chat/completions", data, headers, cancellationToken: token);
            var responseText = chatHttpResponse.choices[0].message["content"].Trim();

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

        public class ChatGPTResponse
        {
            public string id { get; set; }
            public List<Choice> choices { get; set; }
        }

        public class Choice
        {
            public Dictionary<string, string> message { get; set; }
        }
    }
}

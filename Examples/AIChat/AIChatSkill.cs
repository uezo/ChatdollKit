using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Network;

namespace ChatdollKit.Examples.AIChat
{
    public class AIChatSkill : SkillBase
    {
        [Header("Auth")]
        [SerializeField]
        private string apiKey;

        [Header("Chat configuration")]
        [SerializeField]
        private int maxTokens;
        [SerializeField]
        private int temperature;

        private ChatdollHttp client = new ChatdollHttp(timeout: 20000);

        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            var headers = new Dictionary<string, string>()
            {
                { "Authorization", "Bearer " + apiKey }
            };

            var data = new Dictionary<string, object>()
            {
                { "model", "text-davinci-003" },
                { "max_tokens", maxTokens > 0 ? maxTokens : 1000 },
                { "temperature", temperature },
                { "prompt", request.Text }
            };

            var chatHttpResponse = await client.PostJsonAsync<ChatResponse>("https://api.openai.com/v1/completions", data, headers, cancellationToken: token);

            var response = new Response(request.Id);
            response.AddVoiceTTS(chatHttpResponse.choices[0].text);

            return response;
        }

        public class ChatResponse
        {
            public string id { get; set; }
            public string @object { get; set; }
            public int created { get; set; }
            public string model { get; set; }
            public List<Choice> choices { get; set; }
            public Usage usage { get; set; }
        }

        public class Choice
        {
            public string text { get; set; }
            public int index { get; set; }
            public object logprobs { get; set; }
            public string finish_reason { get; set; }
        }

        public class Usage
        {
            public int prompt_tokens { get; set; }
            public int completion_tokens { get; set; }
            public int total_tokens { get; set; }
        }
    }
}

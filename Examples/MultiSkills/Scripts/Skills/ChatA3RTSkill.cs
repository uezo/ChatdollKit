using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Network;

namespace ChatdollKit.Examples.MultiSkills
{
    public class ChatA3RTSkill : SkillBase
    {
        public string A3RTApiKey;
        private ChatdollHttp client = new ChatdollHttp();

        private void OnDestroy()
        {
            client?.Dispose();
        }

        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            var response = new Response(request.Id);

            // Call A3RT API
            var formData = new Dictionary<string, string>()
            {
                { "apikey", A3RTApiKey },
                { "query", request.Text },
            };
            var a3rtResponse = await client.PostFormAsync<A3RTResponse>("https://api.a3rt.recruit-tech.co.jp/talk/v1/smalltalk", formData);

            // Set api result to response
            response.AddVoiceTTS((a3rtResponse?.results?[0]?.reply ?? string.Empty) + "。");

            // Set false to continue chatting after this response
            state.Topic.IsFinished = false;

            return response;
        }

        class A3RTResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public List<A3RTResult> results { get; set; }
        }

        class A3RTResult
        {
            public float perplexity { get; set; }
            public string reply { get; set; }
        }
    }
}

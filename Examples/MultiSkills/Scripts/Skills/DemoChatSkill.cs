using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Network;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.Examples.MultiSkills
{
    public class DemoChatSkill : SkillBase
    {
        private ChatdollHttp client = new ChatdollHttp();

        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            // Build and return response message
            var response = new Response(request.Id);

            // Get chat response message from server
            var encodedText = UnityWebRequest.EscapeURL(request.Text);
            var chatResponse = await client.GetJsonAsync<DemoChatResponse>($"https://api.uezo.net/chat/?input={encodedText}");
            if (chatResponse != null && chatResponse.outputs.Count > 0)
            {
                Random.InitState(System.DateTime.Now.Millisecond);
                response.AddVoiceTTS(chatResponse.outputs[0].val[Random.Range(0, chatResponse.outputs[0].val.Count)]);
                response.EndTopic = false; // Continue chatting
            }
            else
            {
                Debug.LogWarning("No response");
                Debug.LogWarning(chatResponse.outputs.Count);
            }

            return response;
        }

        class DemoChatResponse
        {
            public List<ResponseOutput> outputs;
        }

        class ResponseOutput
        {
            public List<float> score;
            public List<string> val;
        }
    }
}

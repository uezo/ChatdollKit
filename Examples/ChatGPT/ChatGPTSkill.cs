using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Model;
using ChatdollKit.Network;

namespace ChatdollKit.Examples.ChatGPT
{
    public class ChatGPTSkill : SkillBase
    {
        [Header("API configuration")]
        public string ApiKey;
        public string Model = "gpt-3.5-turbo";
        public int MaxTokens = 2000;
        public float Temperature = 0.5f;

        [Header("Context configuration")]
        [TextArea(1, 6)]
        public string ChatCondition;
        [TextArea(1, 3)]
        public string User1stShot;
        [TextArea(1, 3)]
        public string Assistant1stShot;
        public int HistoryTurns = 10;

        protected List<Dictionary<string, string>> histories = new List<Dictionary<string, string>>();
        protected ChatdollHttp client = new ChatdollHttp(timeout: 20000);

        private void Start()
        {
            // This is an example of the animation and face expression while processing request.
            // If you want make multi-skill virtual agent move this code to where common logic should be implemented like main app.
            var processingAnimation = new AnimatedVoiceRequest();
            processingAnimation.AddAnimation("AGIA_Idle_concern_01_right_hand_front", duration: 20.0f);
            processingAnimation.AddFace("Blink");
            var neutralFaceRequest = new FaceRequest();
            neutralFaceRequest.AddFace("Neutral");

            var dialogController = gameObject.GetComponent<DialogController>();
#pragma warning disable CS1998, CS4014
            dialogController.OnRequestAsync = async (request, token) =>
            {
                modelController.AnimatedSay(processingAnimation, token);
            };
            dialogController.OnStartShowingResponseAsync = async (response, token) =>
            {
                modelController.SetFace(neutralFaceRequest, token);
                modelController.StartIdlingAsync();
            };
#pragma warning restore CS1998, CS4014
        }

        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            if (state.IsNew)
            {
                // Clear history and put guide-rail histories when the state is newly created
                histories.Clear();
                if (!string.IsNullOrEmpty(User1stShot))
                {
                    histories.Add(new Dictionary<string, string>() {
                        { "role", "user" },
                        { "content", User1stShot }
                    });
                }
                if (!string.IsNullOrEmpty(Assistant1stShot))
                {
                    histories.Add(new Dictionary<string, string>() {
                        { "role", "assistant" },
                        { "content", Assistant1stShot }
                    });
                }
            }

            // Auth header
            var headers = new Dictionary<string, string>()
            {
                { "Authorization", "Bearer " + ApiKey }
            };

            var messages = new List<Dictionary<string, string>>();

            // Condition
            messages.Add(new Dictionary<string, string>() {
                { "role", "system" },
                { "content", ChatCondition }
            });

            // Histories
            messages.AddRange(histories.Skip(histories.Count - HistoryTurns * 2).ToList());

            // Input
            messages.Add(new Dictionary<string, string>() {
                { "role", "user" },
                { "content", request.Text }
            });

            // Make requesta data
            var data = new Dictionary<string, object>()
            {
                { "model", Model },
                { "max_tokens", MaxTokens },
                { "temperature", Temperature },
                { "messages", messages }
            };

            // Call API
            var chatHttpResponse = await client.PostJsonAsync<ChatGPTResponse>("https://api.openai.com/v1/chat/completions", data, headers, cancellationToken: token);
            var responseText = chatHttpResponse.choices[0].message["content"].Trim();

            // Make chat response
            var response = new Response(request.Id);
            var responseTextToSplit = ParseResponse(responseText).Replace("。", "。|").Replace("！", "！|").Replace("？", "？|").Replace("\n", "");

            foreach (var text in responseTextToSplit.Split('|'))
            {
                if (!string.IsNullOrEmpty(text.Trim()))
                {
                    response.AddVoiceTTS(text);
                    Debug.Log($"Assistant: {text}");
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

        protected virtual string ParseResponse(string responseText)
        {
            // Override this method if you want to parse some data and text to speech from message from OpenAI.

            // e.g. Parse emotion params and response text
            //var responseJson = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseText);

            //var emotions = new Dictionary<string, int>()
            //{
            //    { "joy", int.Parse(responseJson["joy"]) },
            //    { "angry", int.Parse(responseJson["angry"]) },
            //    { "sorrow", int.Parse(responseJson["sorrow"]) },
            //    { "fun", int.Parse(responseJson["fun"]) }
            //};
            //Debug.Log(JsonConvert.SerializeObject(emotions));

            //return responseJson["response_text"];

            return responseText;
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

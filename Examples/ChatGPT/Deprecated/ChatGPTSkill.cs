using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
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
            var processingAnimation = new List<Model.Animation>();
            processingAnimation.Add(new Model.Animation("BaseParam", 3, 0.3f));
            processingAnimation.Add(new Model.Animation("BaseParam", 3, 20.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
            var processingFace = new List<FaceExpression>();
            processingFace.Add(new FaceExpression("Blink", 3.0f));

            var neutralFaceRequest = new List<FaceExpression>();
            neutralFaceRequest.Add(new FaceExpression("Neutral"));

            var dialogController = gameObject.GetComponent<DialogController>();
#pragma warning disable CS1998
            dialogController.OnRequestAsync = async (request, token) =>
#pragma warning restore CS1998
            {
                modelController.StopIdling();
                modelController.Animate(processingAnimation);
                modelController.SetFace(processingFace);
            };
#pragma warning disable CS1998
            dialogController.OnStartShowingResponseAsync = async (response, token) =>
#pragma warning restore CS1998
            {
                modelController.SetFace(neutralFaceRequest);
            };

            var animationOnStart = new List<Model.Animation>();
            animationOnStart.Add(new Model.Animation("BaseParam", 6, 0.5f));
            animationOnStart.Add(new Model.Animation("BaseParam", 10, 3.0f));
            modelController.Animate(animationOnStart);

            var faceOnStart = new List<FaceExpression>();
            faceOnStart.Add(new FaceExpression("Joy", 3.0f));
            modelController.SetFace(faceOnStart);
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
            var chatGPTResponseText = chatHttpResponse.choices[0].message["content"].Trim();

            // Make chat response
            var response = new Response(request.Id);
            UpdateResponse(response, chatGPTResponseText);

            // Update histories
            histories.Add(messages.Last());
            histories.Add(new Dictionary<string, string>() {
                { "role", "assistant" },
                { "content", chatGPTResponseText }
            });

            return response;
        }

        protected virtual Response UpdateResponse(Response response, string chatGPTResponseText)
        {
            // Override this method if you want to parse some data and text to speech from message from OpenAI.

            var responseTextToSplit = chatGPTResponseText.Replace("。", "。|").Replace("！", "！|").Replace("？", "？|").Replace("\n", "");

            foreach (var text in responseTextToSplit.Split('|'))
            {
                if (!string.IsNullOrEmpty(text.Trim()))
                {
                    response.AddVoiceTTS(text);
                    Debug.Log($"Assistant: {text}");
                }
            }
            response.AddAnimation("BaseParam", 6);

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

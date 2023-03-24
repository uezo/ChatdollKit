using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Model;
using ChatdollKit.Network;

namespace ChatdollKit.Extension.Watson
{
    public class WatsonAssistantRequestProcessor : MonoBehaviour, IRequestProcessorWithPrompt
    {
        public string AssistantUrl = string.Empty;
        public string AssistantId = string.Empty;
        public string ApiKey = string.Empty;
        protected ChatdollHttp client = new ChatdollHttp();
        protected ModelController modelController;

        private string sessionId = string.Empty;
        private Dictionary<string, string> authorizationHeader = new Dictionary<string, string>();

        protected virtual void Awake()
        {
            modelController = GetComponent<ModelController>();
            authorizationHeader.Add("Authorization", client.GetBasicAuthenticationHeaderValue("apikey", ApiKey));
        }

        public virtual async UniTask PromptAsync(DialogRequest dialogRequest, CancellationToken token)
        {
            // Create new session on start conversation
            sessionId = await GetSession(token);

            // Send empty message to trigger welcome node
            var watsonPromptResponse = await client.PostJsonAsync<WatsonResponse>(
                $"{AssistantUrl}/v2/assistants/{AssistantId}/sessions/{sessionId}/message?version=2021-06-14",
                new Dictionary<string, string>(),
                headers: authorizationHeader, cancellationToken: token
            );

            var animatedVoice = new AnimatedVoiceRequest();
            animatedVoice.AddVoiceTTS(watsonPromptResponse.output.generic[0].text);

            // Show animated voice
            if (animatedVoice != null)
            {
                await modelController.AnimatedSay(animatedVoice, token);
            }
        }

        protected virtual async UniTask<string> GetSession(CancellationToken token)
        {
            // Create new session
            var watsonSessionResponse = await client.PostJsonAsync<Dictionary<string, string>>(
                $"{AssistantUrl}/v2/assistants/{AssistantId}/sessions?version=2021-06-14",
                new Dictionary<string, string>(),
                headers: authorizationHeader, cancellationToken: token
            );
            Debug.Log($"New Session: {watsonSessionResponse["session_id"]}");

            return watsonSessionResponse["session_id"];
        }

        public virtual async UniTask<Response> ProcessRequestAsync(Request request, CancellationToken token)
        {
            try
            {
                if (!request.IsSet() || request.IsCanceled)
                {
                    return null;
                }

                if (token.IsCancellationRequested) { return null; }

                // Send message to Watson Assistant
                var watsonResponse = await client.PostJsonAsync<WatsonResponse>(
                    $"{AssistantUrl}/v2/assistants/{AssistantId}/sessions/{sessionId}/message?version=2021-06-14",
                    new WatsonRequest() { input = new RequestInput() { text = request.Text, options = new RequestOptions() { return_context = true } } },
                    headers: authorizationHeader, cancellationToken: token
                );

                // Make Response
                var skillResponse = new Response(request.Id);
                skillResponse.AddVoiceTTS(watsonResponse.output.generic[0].text);
                if (
                    watsonResponse.output.generic.Count > 1
                    && watsonResponse.output.generic[1].response_type == "iframe"
                    && !string.IsNullOrEmpty(watsonResponse.output.generic[1].source)
                    )
                {
                    // Add animation and face expression to skillResponse (experimental)
                    var animAndFace = JsonConvert.DeserializeObject<Dictionary<string, string>>(watsonResponse.output.generic[1].source);
                    if (animAndFace.ContainsKey("animation"))
                    {
                        skillResponse.AddAnimation("BaseParam", int.Parse(animAndFace["animation"]));
                    }
                    if (animAndFace.ContainsKey("face"))
                    {
                        skillResponse.AddFace(animAndFace["face"]);
                    }
                }

                // Show Response
                await modelController.AnimatedSay(skillResponse.AnimatedVoiceRequest, token);

                // Control conversation loop
                if (watsonResponse.context.skills["main skill"].user_defined != null
                    && watsonResponse.context.skills["main skill"].user_defined.end_conversation == true)
                {
                    skillResponse.EndConversation = true;
                }

                return skillResponse;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at ProcessRequestAsync: {ex.Message}\n{ex.StackTrace}");

                throw ex;
            }
        }

        // Essential message object for Watson Assistant APIs. Use Watson SDK for advanced use cases.

        class WatsonRequest
        {
            public RequestInput input;
        }

        class RequestInput
        {
            public string text;
            public RequestOptions options;
        }

        class RequestOptions
        {
            public bool return_context;
        }

        class WatsonResponse
        {
            public ResponseOutput output;
            public ResponseContext context;
        }

        class ResponseOutput
        {
            public List<ResponseGeneric> generic;
        }

        class ResponseGeneric
        {
            public string response_type;
            public string text;
            public string title;
            public string source;
        }

        class ResponseContext
        {
            public Dictionary<string, ResponseSkill> skills;
        }

        class ResponseSkill
        {
            public ResponseUserDefined user_defined;
        }

        class ResponseUserDefined
        {
            public bool end_conversation;
        }
    }
}

﻿using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class HttpPrompter : MonoBehaviour
    {
        public string PromptUri;
        private string defaultPromptKey = "DEFAULT_PROMPT_KEY";
        public string PingUri;
        protected Dictionary<string, AnimatedVoiceRequest> promptAnimatedVoices = new Dictionary<string, AnimatedVoiceRequest>();
        protected RequestType promptRequestType = RequestType.Voice;
        protected ChatdollHttp httpClient = new ChatdollHttp();
        protected ModelController modelController;

        protected void Awake()
        {
            modelController = gameObject.GetComponent<ModelController>();
            if (modelController == null)
            {
                Debug.LogError("ModelController is missing. This application will run into crush when start prompt.");
            }
        }

        public async UniTask OnPromptAsync(Request preRequest, User user, State state, CancellationToken token)
        {
            var promptKey = preRequest != null && preRequest.HasIntent() ? preRequest.Intent.Name : defaultPromptKey;
            var promptAnimatedVoice = promptAnimatedVoices.ContainsKey(promptKey) ? promptAnimatedVoices[promptKey] : null;
            if (promptAnimatedVoice == null)
            {
                // Update prompt animated voice and request type
                var httpPromptResponse = await httpClient.PostJsonAsync<HttpPromptResponse>(PromptUri, new HttpPromptRequest(preRequest, state));
                if (httpPromptResponse.Response != null)
                {
                    promptAnimatedVoice = httpPromptResponse.Response.AnimatedVoiceRequest;
                }
                if (httpPromptResponse.State != null)
                {
                    promptRequestType = httpPromptResponse.State.Topic.RequiredRequestType;
                }
            }
            else if (!string.IsNullOrEmpty(PingUri))
            {
#pragma warning disable CS4014
                // Send ping request to warm up
                httpClient.GetAsync(PingUri);
#pragma warning restore CS4014
            }

            // Set request type and show animated voice
            state.Topic.RequiredRequestType = promptRequestType;
            if (promptAnimatedVoice != null)
            {
                await modelController.AnimatedSay(promptAnimatedVoice, token);
                // Add cache after finish successfully
                promptAnimatedVoices[promptKey] = promptAnimatedVoice;
            }
        }

        public void ResetPrompt()
        {
            promptAnimatedVoices.Clear();
            promptRequestType = RequestType.Voice;
        }

        // Request message
        private class HttpPromptRequest
        {
            public Request Request { get; set; }
            public State State { get; set; }

            public HttpPromptRequest(Request request, State state)
            {
                Request = request;
                State = state;
            }
        }

        // Response message
        private class HttpPromptResponse
        {
            public Request Request { get; set; }
            public State State { get; set; }
            public Response Response { get; set; }
            public HttpPromptError Error { get; set; }
        }

        // Error info in response
        private class HttpPromptError
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public string Detail { get; set; }
        }
    }
}

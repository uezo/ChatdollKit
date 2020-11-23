using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Model;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class HttpPrompter : MonoBehaviour
    {
        public string PromptUri;
        public string PingUri;
        protected AnimatedVoiceRequest promptAnimatedVoice;
        protected RequestType promptRequestType = RequestType.Voice;
        protected ChatdollHttp httpClient;
        protected ModelController modelController;

        protected void Awake()
        {
            httpClient = new ChatdollHttp();
            modelController = gameObject.GetComponent<ModelController>();
            if (modelController == null)
            {
                Debug.LogError("ModelController is missing. This application will run into crush when start prompt.");
            }
        }

        protected void OnDestroy()
        {
            httpClient?.Dispose();
        }

        public async Task OnPromptAsync(User user, Context context, CancellationToken token)
        {
            if (promptAnimatedVoice == null)
            {
                // Update prompt animated voice and request type
                var httpPromptResponse = await httpClient.PostJsonAsync<HttpPromptResponse>(PromptUri, new HttpPromptRequest(context));
                if (httpPromptResponse.Response != null)
                {
                    promptAnimatedVoice = httpPromptResponse.Response.AnimatedVoiceRequest;
                }
                if (httpPromptResponse.Context != null)
                {
                    promptRequestType = httpPromptResponse.Context.Topic.RequiredRequestType;
                }
            }
            else if (!string.IsNullOrEmpty(PingUri))
            {
#pragma warning disable CS4014
                // Send ping request to warm up
                httpClient.GetJsonAsync<AnimatedVoiceRequest>(PingUri);
#pragma warning restore CS4014
            }

            // Set request type and show animated voice
            context.Topic.RequiredRequestType = promptRequestType;
            if (promptAnimatedVoice != null)
            {
                await modelController.AnimatedSay(promptAnimatedVoice, token);
            }
        }

        public void ResetPrompt()
        {
            promptAnimatedVoice = null;
            promptRequestType = RequestType.Voice;
        }

        // Request message
        private class HttpPromptRequest
        {
            public Context Context { get; set; }

            public HttpPromptRequest(Context context)
            {
                Context = context;
            }
        }

        // Response message
        private class HttpPromptResponse
        {
            public Context Context { get; set; }
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

using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Model;
using ChatdollKit.Network;
using System.Collections.Generic;

namespace ChatdollKit.Dialog
{
    public class HttpPrompter : MonoBehaviour
    {
        public string PromptUri;
        private string defaultPromptKey = "DEFAULT_PROMPT_KEY";
        public string PingUri;
        protected Dictionary<string, AnimatedVoiceRequest> promptAnimatedVoices = new Dictionary<string, AnimatedVoiceRequest>();
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

        public async Task OnPromptAsync(Request preRequest, User user, Context context, CancellationToken token)
        {
            var promptKey = preRequest != null && !string.IsNullOrEmpty(preRequest.Intent) ? preRequest.Intent : defaultPromptKey;
            var promptAnimatedVoice = promptAnimatedVoices.ContainsKey(promptKey) ? promptAnimatedVoices[promptKey] : null;
            if (promptAnimatedVoice == null)
            {
                // Update prompt animated voice and request type
                var httpPromptResponse = await httpClient.PostJsonAsync<HttpPromptResponse>(PromptUri, new HttpPromptRequest(preRequest, context));
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
            public Context Context { get; set; }

            public HttpPromptRequest(Request request, Context context)
            {
                Request = request;
                Context = context;
            }
        }

        // Response message
        private class HttpPromptResponse
        {
            public Request Request { get; set; }
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

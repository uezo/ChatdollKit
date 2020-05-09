using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Model;
using ChatdollKit.Network;


namespace ChatdollKit.Dialog
{
    public class HttpDialogProcessorBase : MonoBehaviour, IDialogProcessor
    {
        public string Name;
        public string DialogUri;
        protected ModelController modelController;
        protected ChatdollHttp httpClient;

        protected virtual void Awake()
        {
            modelController = gameObject.GetComponent<ModelController>();
            httpClient = new ChatdollHttp();
        }

        private void OnDestroy()
        {
            httpClient?.Dispose();
        }

        // Get topic name
        public virtual string TopicName
        {
            get
            {
                // Use Name if configured
                if (!string.IsNullOrEmpty(Name))
                {
                    return Name;
                }

                // Create name from ClassName
                var name = GetType().Name;
                if (name.ToLower().EndsWith("dialogprocessor"))
                {
                    name = name.Substring(0, name.Length - 15);
                }
                else if (name.ToLower().EndsWith("dialog"))
                {
                    name = name.Substring(0, name.Length - 6);
                }
                return name.ToLower();
            }
        }

        public virtual void Configure()
        {
            //
        }

        // Process dialog on server
        public virtual async Task<Response> ProcessAsync(Request request, Context context, CancellationToken token)
        {
            var httpDialogResponse = await httpClient.PostJsonAsync<HttpDialogResponse>(DialogUri, new HttpDialogRequest(request, context));

            // Update topic
            context.Topic.Status = httpDialogResponse.Context.Topic.Status;
            context.Topic.ContinueTopic = httpDialogResponse.Context.Topic.ContinueTopic;
            context.Topic.RequiredRequestType = httpDialogResponse.Context.Topic.RequiredRequestType;

            // Update data
            context.Data = httpDialogResponse.Context.Data;

            return httpDialogResponse.Response;
        }

        // Show response
        public virtual async Task ShowResponseAsync(Response response, Request request, Context context, CancellationToken token)
        {
            var animatedVoiceRequest = (response as AnimatedVoiceResponse).AnimatedVoiceRequest;

            if (animatedVoiceRequest != null)
            {
                await modelController?.AnimatedSay(animatedVoiceRequest, token);
            }
        }

        // Request message
        private class HttpDialogRequest
        {
            public Request Request { get; set; }
            public Context Context { get; set; }

            public HttpDialogRequest(Request request, Context context)
            {
                Request = request;
                Context = context;
            }
        }

        // Response message
        private class HttpDialogResponse
        {
            public Request Request { get; set; }
            public Context Context { get; set; }
            public AnimatedVoiceResponse Response { get; set; }
            public HttpDialogError Error { get; set; }
        }

        // Response with AnimatedRequest
        public class AnimatedVoiceResponse : Response
        {
            public AnimatedVoiceRequest AnimatedVoiceRequest { get; set; }
            public AnimatedVoiceResponse(string id) : base(id) { }
        }

        // Error info in response
        private class HttpDialogError
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public Exception Exception { get; set; }
        }
    }
}

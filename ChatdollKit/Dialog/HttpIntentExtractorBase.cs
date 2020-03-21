using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using ChatdollKit.Model;


namespace ChatdollKit.Dialog
{
    public class HttpIntentExtractorBase : MonoBehaviour, IIntentExtractor
    {
        public string IntentExtractorUri;
        private ModelController modelController;

        protected virtual void Awake()
        {
            modelController = gameObject.GetComponent<ModelController>();
        }

        public virtual void Configure()
        {
            //
        }

        public virtual async Task<Response> ExtractIntentAsync(Request request, Context context, CancellationToken token)
        {
            // Call intent extractor
            var data = JsonConvert.SerializeObject(new HttpIntentRequest(request, context));
            var content = new StringContent(data, Encoding.UTF8, "application/json");
            using (var client = new HttpClient())
            {
                var response = await client.PostAsync(IntentExtractorUri, content);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Error occured while calling intent extraction service ({response.StatusCode.ToString()})");
                }
                var responseContent = await response.Content.ReadAsStringAsync();
                var httpIntentResponse = JsonConvert.DeserializeObject<HttpIntentResponse>(responseContent);

                // Update request
                request.Intent = httpIntentResponse.Request.Intent;
                request.IntentPriority = httpIntentResponse.Request.IntentPriority;
                request.Words = httpIntentResponse.Request.Words ?? request.Words;
                request.Entities = httpIntentResponse.Request.Entities ?? request.Entities;
                request.IsAdhoc = httpIntentResponse.Request.IsAdhoc;

                return httpIntentResponse.Response;
            }
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
        public class HttpIntentRequest
        {
            public Request Request { get; set; }
            public Context Context { get; set; }

            public HttpIntentRequest(Request request, Context context)
            {
                Request = request;
                Context = context;
            }
        }

        // Response message
        public class HttpIntentResponse
        {
            public Request Request { get; set; }
            public Context Context { get; set; }
            public AnimatedVoiceResponse Response { get; set; }
            public HttpIntentError Error { get; set; }
        }

        // Response with AnimatedRequest
        public class AnimatedVoiceResponse : Response
        {
            public AnimatedVoiceRequest AnimatedVoiceRequest { get; set; }
            public AnimatedVoiceResponse(string id) : base(id) { }
        }

        // Error info in response
        public class HttpIntentError
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public Exception Exception { get; set; }
        }
    }
}

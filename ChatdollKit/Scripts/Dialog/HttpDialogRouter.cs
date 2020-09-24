using System;
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class HttpDialogRouter : DialogRouterBase
    {
        public string IntentExtractorUri;
        protected ChatdollHttp httpClient = new ChatdollHttp();

        private void OnDestroy()
        {
            httpClient?.Dispose();
        }

        public override async Task<Response> ExtractIntentAsync(Request request, Context context, CancellationToken token)
        {
            var httpIntentResponse = await httpClient.PostJsonAsync<HttpIntentResponse>(
                IntentExtractorUri, new HttpIntentRequest(request, context));

            // Update request
            request.Intent = httpIntentResponse.Request.Intent;
            request.IntentPriority = httpIntentResponse.Request.IntentPriority;
            request.Words = httpIntentResponse.Request.Words ?? request.Words;
            request.Entities = httpIntentResponse.Request.Entities ?? request.Entities;
            request.IsAdhoc = httpIntentResponse.Request.IsAdhoc;

            return httpIntentResponse.Response;
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
            public Response Response { get; set; }
            public HttpIntentError Error { get; set; }
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

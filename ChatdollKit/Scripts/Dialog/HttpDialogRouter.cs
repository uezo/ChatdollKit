using System;
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class HttpSkillRouter : SkillRouterBase
    {
        public string IntentExtractorUri;
        public string DialogProcessorUriBase;
        protected ChatdollHttp httpClient = new ChatdollHttp();

        private void OnDestroy()
        {
            httpClient?.Dispose();
        }

        public override async Task ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            var httpIntentResponse = await httpClient.PostJsonAsync<HttpIntentResponse>(
                IntentExtractorUri, new HttpIntentRequest(request, state));

            // Update request
            request.Intent = httpIntentResponse.Request.Intent;
            request.IntentPriority = httpIntentResponse.Request.IntentPriority;
            request.Words = httpIntentResponse.Request.Words ?? request.Words;
            request.Entities = httpIntentResponse.Request.Entities ?? request.Entities;
            request.IsAdhoc = httpIntentResponse.Request.IsAdhoc;

            // Update state data
            state.Data = httpIntentResponse.State.Data;
        }

        public override ISkill Route(Request request, State state, CancellationToken token)
        {
            // Register DialogProcessor dynamically
            if (!topicResolver.ContainsKey(request.Intent))
            {
                var skill = gameObject.AddComponent<HttpSkillBase>();
                skill.Name = request.Intent;
                skill.DialogUri = DialogProcessorUriBase.EndsWith("/") ?
                    DialogProcessorUriBase + request.Intent : DialogProcessorUriBase + "/" + request.Intent;
                RegisterSkill(skill);
            }

            return base.Route(request, state, token);
        }

        // Request message
        private class HttpIntentRequest
        {
            public Request Request { get; set; }
            public State State { get; set; }

            public HttpIntentRequest(Request request, State state)
            {
                Request = request;
                State = state;
            }
        }

        // Response message
        private class HttpIntentResponse
        {
            public Request Request { get; set; }
            public State State { get; set; }
            public Response Response { get; set; }
            public HttpIntentError Error { get; set; }
        }

        // Error info in response
        private class HttpIntentError
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public Exception Exception { get; set; }
        }
    }
}

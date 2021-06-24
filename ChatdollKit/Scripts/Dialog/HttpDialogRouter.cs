using System;
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class HttpSkillRouter : SkillRouterBase
    {
        public string IntentExtractorUri;
        public string SkillUriBase;
        protected ChatdollHttp httpClient = new ChatdollHttp();

        private void OnDestroy()
        {
            httpClient?.Dispose();
        }

        public override async Task<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            var httpIntentResponse = await httpClient.PostJsonAsync<HttpIntentResponse>(
                IntentExtractorUri, new HttpIntentRequest(request, state));

            if (httpIntentResponse.Error != null)
            {
                throw httpIntentResponse.Error.Exception;
            }

            return httpIntentResponse.IntentExtractionResult;
        }

        public override ISkill Route(Request request, State state, CancellationToken token)
        {
            // Register skill dynamically
            if (!topicResolver.ContainsKey(request.Intent.Name))
            {
                var skill = gameObject.AddComponent<HttpSkillBase>();
                skill.Name = request.Intent.Name;
                skill.DialogUri = SkillUriBase.EndsWith("/") ?
                    SkillUriBase + request.Intent.Name : SkillUriBase + "/" + request.Intent.Name;
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
            public IntentExtractionResult IntentExtractionResult { get; set; }
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

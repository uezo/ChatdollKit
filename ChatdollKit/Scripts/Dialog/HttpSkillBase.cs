using System;
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class HttpSkillBase : SkillBase
    {
        public string Uri;
        protected ChatdollHttp httpClient = new ChatdollHttp();

        private void OnDestroy()
        {
            httpClient?.Dispose();
        }

        public override async Task<Response> PreProcessAsync(Request request, State state, CancellationToken token)
        {
            var httpSkillResponse = await httpClient.PostJsonAsync<HttpSkillResponse>(Uri, new HttpSkillRequest(request, state, true));

            if (httpSkillResponse.State != null)
            {
                // Update status and data
                state.Topic.Status = httpSkillResponse.State.Topic.Status;
                state.Data = httpSkillResponse.State.Data;
            }

            return httpSkillResponse.Response;
        }

        // Process skill on server
        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            var httpSkillResponse = await httpClient.PostJsonAsync<HttpSkillResponse>(Uri, new HttpSkillRequest(request, state));

            // Update topic
            state.Topic.Status = httpSkillResponse.State.Topic.Status;
            state.Topic.IsFinished = httpSkillResponse.State.Topic.IsFinished;
            state.Topic.RequiredRequestType = httpSkillResponse.State.Topic.RequiredRequestType;

            // Update data
            state.Data = httpSkillResponse.State.Data;

            // Update user info
            request.User.Name = httpSkillResponse.User.Name;
            request.User.Nickname = httpSkillResponse.User.Nickname;
            request.User.Data = httpSkillResponse.User.Data;

            return httpSkillResponse.Response;
        }

        // Request message
        private class HttpSkillRequest
        {
            public Request Request { get; set; }
            public State State { get; set; }
            public bool PreProcess { get; set; }

            public HttpSkillRequest(Request request, State state, bool preProcess = false)
            {
                Request = request;
                State = state;
                PreProcess = preProcess;
            }
        }

        // Response message
        private class HttpSkillResponse
        {
            public Response Response { get; set; }
            public State State { get; set; }
            public User User { get; set; }
            public HttpSkillError Error { get; set; }
        }

        // Error info in response
        private class HttpSkillError
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public Exception Exception { get; set; }
        }
    }
}

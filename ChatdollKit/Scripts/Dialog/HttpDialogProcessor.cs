using System;
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class HttpSkillBase : SkillBase
    {
        public string DialogUri;
        protected ChatdollHttp httpClient = new ChatdollHttp();

        private void OnDestroy()
        {
            httpClient?.Dispose();
        }

        public override async Task<Response> PreProcessAsync(Request request, State state, CancellationToken token)
        {
            var httpDialogResponse = await httpClient.PostJsonAsync<HttpDialogResponse>(DialogUri, new HttpDialogRequest(request, state, true));

            if (httpDialogResponse.State != null)
            {
                // Update status and data
                state.Topic.Status = httpDialogResponse.State.Topic.Status;
                state.Data = httpDialogResponse.State.Data;
            }

            return httpDialogResponse.Response;
        }

        // Process dialog on server
        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            var httpDialogResponse = await httpClient.PostJsonAsync<HttpDialogResponse>(DialogUri, new HttpDialogRequest(request, state));

            // Update topic
            state.Topic.Status = httpDialogResponse.State.Topic.Status;
            state.Topic.ContinueTopic = httpDialogResponse.State.Topic.ContinueTopic;
            state.Topic.RequiredRequestType = httpDialogResponse.State.Topic.RequiredRequestType;

            // Update data
            state.Data = httpDialogResponse.State.Data;

            // Update user info
            request.User.Name = httpDialogResponse.Request.User.Name;
            request.User.Nickname = httpDialogResponse.Request.User.Nickname;
            request.User.Data = httpDialogResponse.Request.User.Data;

            return httpDialogResponse.Response;
        }

        // Request message
        private class HttpDialogRequest
        {
            public Request Request { get; set; }
            public State State { get; set; }
            public bool PreProcess { get; set; }

            public HttpDialogRequest(Request request, State state, bool preProcess = false)
            {
                Request = request;
                State = state;
                PreProcess = preProcess;
            }
        }

        // Response message
        private class HttpDialogResponse
        {
            public Request Request { get; set; }
            public State State { get; set; }
            public Response Response { get; set; }
            public HttpDialogError Error { get; set; }
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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
#pragma warning disable CS1998
    class StaticIntentExtractor : IIntentExtractor
    {
        private string intent { get; }

        public StaticIntentExtractor(string intent)
        {
            this.intent = intent;
        }

        public void Configure()
        {

        }

        public async Task<Response> ExtractIntentAsync(Request request, Context context, CancellationToken token)
        {
            request.Intent = intent;
            return new Response(request.Id);
        }

        public async Task ShowResponseAsync(Response response, Request request, Context context, CancellationToken token)
        {

        }
    }
}

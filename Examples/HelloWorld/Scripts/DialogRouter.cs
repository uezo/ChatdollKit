using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Model;

namespace ChatdollKit.Examples.HelloWorld
{
    public class DialogRouter : DialogRouterBase
    {
        // Extract intent and entities from request and context
        public override async Task<Response> ExtractIntentAsync(Request request, Context context, CancellationToken token)
        {
            // Static set hello
            request.Intent = "hello";

            // Model actions for this intent
            var animatedVoiceRequest = new AnimatedVoiceRequest();
            animatedVoiceRequest.AddVoice("line-girl1-haihaai1", preGap: 1.0f, postGap: 2.0f);
            animatedVoiceRequest.AddAnimation("Default");

            // Build and return response message
            var response = new Response(request.Id);
            response.AnimatedVoiceRequest = animatedVoiceRequest;
            return response;
        }
    }
}

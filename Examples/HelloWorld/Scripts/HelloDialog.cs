using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Model;


namespace ChatdollKit.Examples.HelloWorld
{
    public class HelloDialog : DialogProcessorBase
    {
        public override async Task<Response> ProcessAsync(Request request, Context context, CancellationToken token)
        {
            // 
            // Put your application logic here
            // 

            // Model actions to express the result of application logic
            var animatedVoiceRequest = new AnimatedVoiceRequest();
            animatedVoiceRequest.AddVoice("line-girl1-konnichiha1", preGap: 1.0f, postGap: 2.0f);
            animatedVoiceRequest.AddAnimation("Default");

            // Build and return response message
            var response = new Response(request.Id);
            response.AnimatedVoiceRequest = animatedVoiceRequest;
            return response;
        }
    }
}

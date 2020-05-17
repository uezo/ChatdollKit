using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Model;


namespace ChatdollKit.Examples.HelloWorld
{
    public class IntentExtractor : MonoBehaviour, IIntentExtractor
    {
        private ModelController modelController;

        protected virtual void Awake()
        {
            modelController = gameObject.GetComponent<ModelController>();
        }

        public void Configure()
        {

        }

        // Extract intent and entities from request and context
        public async Task<Response> ExtractIntentAsync(Request request, Context context, CancellationToken token)
        {
            // Static set hello
            request.Intent = "hello";

            // Model actions for this intent
            var animatedVoiceRequest = new AnimatedVoiceRequest();
            animatedVoiceRequest.AddVoice("line-girl1-haihaai1", preGap: 1.0f, postGap: 2.0f);
            animatedVoiceRequest.AddAnimation("Default");

            // Build and return response message
            var response = new Response(request.Id);
            response.Payloads = animatedVoiceRequest;
            return response;
        }

        // Show the actions
        public async Task ShowResponseAsync(Response response, Request request, Context context, CancellationToken token)
        {
            var animatedVoiceRequest = response.Payloads as AnimatedVoiceRequest;
            await modelController.AnimatedSay(animatedVoiceRequest, token);
        }
    }
}

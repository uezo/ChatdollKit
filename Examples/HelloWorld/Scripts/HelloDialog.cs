using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.HelloWorld
{
    public class HelloDialog : DialogProcessorBase
    {
        public override async Task<Response> ProcessAsync(Request request, Context context, CancellationToken token)
        {
            // 
            // Put your application logic here
            // 

            // Build and return response message
            var response = new Response(request.Id);
            response.AddVoice("line-girl1-konnichiha1", preGap: 1.0f, postGap: 2.0f);
            response.AddAnimation("Default");
            return response;
        }
    }
}

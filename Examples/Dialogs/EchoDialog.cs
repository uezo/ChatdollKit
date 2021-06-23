using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.Dialogs
{
    public class EchoDialog : SkillBase
    {
        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            // Build and return response message
            var response = new Response(request.Id);

            // Set what user said to response
            response.AddVoiceTTS($"{request.Text}");

            return response;
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Model;

namespace ChatdollKit.Examples.HelloWorld
{
    public class DialogRouter : DialogRouterBase
    {
        // Extract intent and entities from request and context
        public override async Task ExtractIntentAsync(Request request, Context context, CancellationToken token)
        {
            // Always set hello
            request.Intent = "hello";
        }
    }
}

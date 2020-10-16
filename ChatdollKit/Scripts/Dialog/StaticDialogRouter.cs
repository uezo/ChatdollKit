using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public class StaticDialogRouter : DialogRouterBase
    {
#pragma warning disable CS1998
        public override async Task ExtractIntentAsync(Request request, Context context, CancellationToken token)
        {
            if (intentResolver.Count == 1)
            {
                request.Intent = intentResolver.First().Key;
            }
            else
            {
                request.Intent = string.Empty;
            }
        }
#pragma warning restore CS1998
    }
}

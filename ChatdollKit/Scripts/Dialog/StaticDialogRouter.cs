using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public class StaticDialogRouter : SkillRouterBase
    {
#pragma warning disable CS1998
        public override async Task ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            if (topicResolver.Count == 1)
            {
                request.Intent = topicResolver.First().Key;
            }
            else
            {
                request.Intent = string.Empty;
            }
        }
#pragma warning restore CS1998
    }
}

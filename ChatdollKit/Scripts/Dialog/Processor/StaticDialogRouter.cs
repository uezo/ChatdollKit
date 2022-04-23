using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog.Processor
{
    public class StaticSkillRouter : SkillRouterBase
    {
#pragma warning disable CS1998
        public override async UniTask<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            if (topicResolver.Count == 1)
            {
                return new IntentExtractionResult(topicResolver.First().Key);
            }
            else
            {
                return null;
            }
        }
#pragma warning restore CS1998
    }
}

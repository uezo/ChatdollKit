using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog.Processor
{
    public interface ISkillRouter
    {
        List<ISkill> RegisterSkills();
        UniTask<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token);
        ISkill Route(Request request, State state, CancellationToken token);
    }
}

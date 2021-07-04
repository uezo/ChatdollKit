using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface ISkillRouter
    {
        void Configure();
        void RegisterSkill(ISkill skill);
        Task<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token);
        ISkill Route(Request request, State state, CancellationToken token);
    }
}

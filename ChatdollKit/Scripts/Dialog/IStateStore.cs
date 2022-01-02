using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IStateStore
    {
        UniTask<State> GetStateAsync(string userId);
        UniTask SaveStateAsync(State state);
        UniTask DeleteStateAsync(string userId);
    }
}

using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface ISkill
    {
        string TopicName { get; }
        bool IsAvailable { get; }
        UniTask<Response> PreProcessAsync(Request request, State state, CancellationToken token);
        UniTask ShowWaitingAnimationAsync(Response response, Request request, State state, CancellationToken token);
        UniTask<Response> ProcessAsync(Request request, State state, CancellationToken token);
        UniTask ShowResponseAsync(Response response, Request request, State state, CancellationToken token);
    }
}

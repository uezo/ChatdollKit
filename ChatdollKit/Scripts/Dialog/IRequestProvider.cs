using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IRequestProvider
    {
        RequestType RequestType { get; }
        UniTask<Request> GetRequestAsync(User user, State state, CancellationToken token, Request preRequest = null);
    }
}

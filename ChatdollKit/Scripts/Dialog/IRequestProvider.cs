using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IRequestProvider
    {
        RequestType RequestType { get; }
        UniTask<Request> GetRequestAsync(CancellationToken token);
    }
}

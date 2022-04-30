using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog.Processor
{
    public interface IRequestProcessor
    {
        UniTask<Response> ProcessRequestAsync(Request request, CancellationToken token);
    }
}

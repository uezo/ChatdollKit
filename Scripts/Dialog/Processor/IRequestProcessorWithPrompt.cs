using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog.Processor
{
    public interface IRequestProcessorWithPrompt: IRequestProcessor
    {
        UniTask PromptAsync(DialogRequest dialogRequest, CancellationToken token);
    }
}

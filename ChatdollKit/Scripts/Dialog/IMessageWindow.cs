using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IMessageWindow
    {
        void Show(string prompt = null);
        void Hide();
        UniTask ShowMessageAsync(string message, CancellationToken token);
        UniTask SetMessageAsync(string message, CancellationToken token);
    }
}

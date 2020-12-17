using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IMessageWindow
    {
        void Show(string prompt = null);
        void Hide();
        Task ShowMessageAsync(string message, CancellationToken token);
        Task SetMessageAsync(string message, CancellationToken token);
    }
}

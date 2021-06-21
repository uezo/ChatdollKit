using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IDialogRouter
    {
        void Configure();
        void RegisterIntent(string intentName, IDialogProcessor dialogProcessor);
        Task ExtractIntentAsync(Request request, State state, CancellationToken token);
        IDialogProcessor Route(Request request, State state, CancellationToken token);
    }
}

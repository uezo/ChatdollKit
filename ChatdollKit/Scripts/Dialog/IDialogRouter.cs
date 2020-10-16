using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IDialogRouter
    {
        void Configure();
        void RegisterIntent(string intentName, IDialogProcessor dialogProcessor);
        Task<Response> ExtractIntentAsync(Request request, Context context, CancellationToken token);
        IDialogProcessor Route(Request request, Context context, CancellationToken token);
    }
}

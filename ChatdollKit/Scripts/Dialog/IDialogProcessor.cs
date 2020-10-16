using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IDialogProcessor
    {
        string TopicName { get; }
        void Configure();
        Task ShowWaitingAnimationAsync(Request request, Context context, CancellationToken token);
        Task<Response> ProcessAsync(Request request, Context context, CancellationToken token);
        Task ShowResponseAsync(Response response, Request request, Context context, CancellationToken token);
    }
}

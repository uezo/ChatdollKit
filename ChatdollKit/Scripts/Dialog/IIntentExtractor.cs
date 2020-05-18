using System.Threading;
using System.Threading.Tasks;


namespace ChatdollKit.Dialog
{
    public interface IIntentExtractor
    {
        void Configure();
        Task<Response> ExtractIntentAsync(Request request, Context context, CancellationToken token);
        Task ShowResponseAsync(Response response, Request request, Context context, CancellationToken token);
    }
}

using System.Threading;
using System.Threading.Tasks;


namespace ChatdollKit.Dialog
{
    public interface IRequestProvider
    {
        RequestType RequestType { get; }
        Task<Request> GetRequestAsync(User user, Context context, CancellationToken token, Request preRequest = null);
    }
}

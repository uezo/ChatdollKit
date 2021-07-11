using System.Threading;
using System.Threading.Tasks;


namespace ChatdollKit.Dialog
{
    public interface IRequestProvider
    {
        RequestType RequestType { get; }
        Task<Request> GetRequestAsync(User user, State state, CancellationToken token, Request preRequest = null);
    }
}

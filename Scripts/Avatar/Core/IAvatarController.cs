using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Avatar
{
    /// <summary>Presentation of one already synthesized response. Implementations marshal device calls as needed.</summary>
    public interface IAvatarController
    {
        /// <summary>Completes when the requested avatar actions finish, including audio. Cancellation stops these actions.</summary>
        UniTask PresentAsync(AvatarRequest request, CancellationToken cancellationToken);
        UniTask StopAsync(CancellationToken cancellationToken = default);
    }
}

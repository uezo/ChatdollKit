using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IUserStore
    {
        UniTask<User> GetUserAsync(string userId);
        UniTask SaveUserAsync(User user);
        UniTask DeleteUserAsync(string userId);
    }
}

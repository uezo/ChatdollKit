using System.Threading.Tasks;


namespace ChatdollKit.Dialog
{
    public interface IUserStore
    {
        Task<User> GetUserAsync(string userId);
        Task SaveUserAsync(User user);
        Task DeleteUserAsync(string userId);
    }
}

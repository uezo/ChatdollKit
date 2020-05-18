using System.Threading.Tasks;


namespace ChatdollKit.Dialog
{
    public interface IContextStore
    {
        Task<Context> GetContextAsync(string userId);
        Task SaveContextAsync(Context context);
        Task DeleteContextAsync(string userId);
    }
}

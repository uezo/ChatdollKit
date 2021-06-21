using System.Threading.Tasks;


namespace ChatdollKit.Dialog
{
    public interface IStateStore
    {
        Task<State> GetStateAsync(string userId);
        Task SaveStateAsync(State state);
        Task DeleteStateAsync(string userId);
    }
}

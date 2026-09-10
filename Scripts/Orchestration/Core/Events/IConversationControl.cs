namespace ChatdollKit.Orchestration
{
    /// <summary>Optional interaction supported by a conversation source. Read-only publishers need not implement it.</summary>
    public interface IConversationControl
    {
        void Interrupt();
    }
}

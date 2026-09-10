namespace ChatdollKit.UI.MessageWindow
{
    /// <summary>A display surface independent of conversation and avatar processing.</summary>
    public interface IMessageWindow
    {
        string MessageId { get; }
        bool IsVisible { get; }
        bool IsAnimating { get; }
        void SetMessage(MessageWindowMessage message);
        void Hide();
    }
}

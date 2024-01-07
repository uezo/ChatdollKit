using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IRequestProvider
    {
        RequestType RequestType { get; }
        UniTask<Request> GetRequestAsync(CancellationToken token);
    }

    public interface IVoiceRequestProvider: IRequestProvider
    {
        void SetMessageWindow(IMessageWindow messageWindow);
        void SetCancelWord(string cancelWord);
        string TextInput { get; set; }
        bool IsListening { get; }
    }
}

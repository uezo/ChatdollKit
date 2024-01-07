using System;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IWakeWordListener
    {
        void SetWakeWord(WakeWord wakeWord);
        void SetCancelWord(string cancelWord);
        void StartListening();
        void StopListening();
        Func<WakeWord, UniTask> OnWakeAsync { get; set; }
        Func<UniTask> OnCancelAsync { get; set; }
        Func<bool> ShouldRaiseThreshold { get; set; }
        bool IsListening { get; }
        string TextInput { get; set; }
    }
}

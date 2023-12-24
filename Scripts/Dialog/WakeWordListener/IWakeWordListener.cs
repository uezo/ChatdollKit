using System;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface IWakeWordListener
    {
        void SetWakeWord(WakeWord wakeWord);
        void SetCancelWord(string cancelWord);
        Func<WakeWord, UniTask> OnWakeAsync { get; set; }
        Func<UniTask> OnCancelAsync { get; set; }
        Func<bool> ShouldRaiseThreshold { get; set; }
        string TextInput { get; set; }
    }
}

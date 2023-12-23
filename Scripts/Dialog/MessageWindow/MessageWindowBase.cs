using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    // Unity2018 support (inspector does not support interface)
    public class MessageWindowBase : MonoBehaviour, IMessageWindow
    {
        public bool IsInstance = true;

        public virtual void Show(string prompt = null)
        {
            throw new System.NotImplementedException();
        }

        public virtual void Hide()
        {
            throw new System.NotImplementedException();
        }

        public virtual UniTask ShowMessageAsync(string message, CancellationToken token)
        {
            throw new System.NotImplementedException();
        }

        public virtual UniTask SetMessageAsync(string message, CancellationToken token)
        {
            throw new System.NotImplementedException();
        }
    }
}

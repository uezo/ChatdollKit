using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ChatdollKit.Dialog
{
    // Unity2018 support (inspector does not support interface)
    public class MessageWindowBase : MonoBehaviour, IMessageWindow
    {
        public virtual void Show(string prompt = null)
        {
            throw new System.NotImplementedException();
        }

        public virtual void Hide()
        {
            throw new System.NotImplementedException();
        }

        public virtual Task ShowMessageAsync(string message, CancellationToken token)
        {
            throw new System.NotImplementedException();
        }

        public virtual Task SetMessageAsync(string message, CancellationToken token)
        {
            throw new System.NotImplementedException();
        }
    }
}

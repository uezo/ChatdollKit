using System;
using System.Runtime.CompilerServices;
using UnityEngine.Networking;


namespace ChatdollKit.Network
{
    public class UnityWebRequestAwaiter : INotifyCompletion
    {
        private UnityWebRequestAsyncOperation asyncOperation { get; }

        public bool IsCompleted
        {
            get { return asyncOperation.isDone; }
        }

        public UnityWebRequestAwaiter(UnityWebRequestAsyncOperation asyncOperation)
        {
            this.asyncOperation = asyncOperation;
        }

        public void GetResult()
        {
            // www returns nothing (void)
        }

        public void OnCompleted(Action continuation)
        {
            asyncOperation.completed += _ => { continuation(); };
        }
    }
}

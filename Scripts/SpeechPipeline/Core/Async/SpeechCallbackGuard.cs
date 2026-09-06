using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.Async
{
    /// <summary>Rejects direct recursive calls from application callbacks without marking unrelated operations during an await.</summary>
    public sealed class SpeechCallbackGuard
    {
        [ThreadStatic] private static HashSet<SpeechCallbackGuard> active;
        public void ThrowIfActive(string message)
        {
            if (active != null && active.Contains(this)) throw new InvalidOperationException(message);
        }
        public void Invoke(Action callback) => Invoke(() => { callback(); return true; });
        public T Invoke<T>(Func<T> callback)
        {
            if (active == null) active = new HashSet<SpeechCallbackGuard>();
            var added = active.Add(this);
            try { return callback(); }
            finally { if (added) active.Remove(this); }
        }
        public UniTask InvokeAsync(Func<UniTask> callback) => Invoke(callback);
        public UniTask<T> InvokeAsync<T>(Func<UniTask<T>> callback) => Invoke(callback);
    }
}

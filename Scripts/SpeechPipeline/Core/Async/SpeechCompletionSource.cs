using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.Async
{
    /// <summary>A shared completion. State changes immediately; registered continuations run on the speech scheduler.</summary>
    public sealed class SpeechCompletionSource<T> : IUniTaskSource<T>
    {
        private readonly object sync = new object();
        private UniTaskStatus status;
        private T result;
        private ExceptionDispatchInfo error;
        private List<Action> continuations;

        public UniTask<T> Task => new UniTask<T>(this, 0);
        public bool TrySetResult(T value) => Complete(UniTaskStatus.Succeeded, value, null);
        public bool TrySetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            return exception is OperationCanceledException canceled
                ? TrySetCanceled(canceled.CancellationToken)
                : Complete(UniTaskStatus.Faulted, default, ExceptionDispatchInfo.Capture(exception));
        }
        public bool TrySetCanceled(CancellationToken token = default) =>
            Complete(UniTaskStatus.Canceled, default, ExceptionDispatchInfo.Capture(new OperationCanceledException(token)));
        public void SetResult(T value) { if (!TrySetResult(value)) throw new InvalidOperationException("Completion was already set."); }
        public void SetException(Exception exception) { if (!TrySetException(exception)) throw new InvalidOperationException("Completion was already set."); }
        public void SetCanceled(CancellationToken token = default) { if (!TrySetCanceled(token)) throw new InvalidOperationException("Completion was already set."); }

        private bool Complete(UniTaskStatus next, T value, ExceptionDispatchInfo failure)
        {
            List<Action> pending;
            lock (sync)
            {
                if (status != UniTaskStatus.Pending) return false;
                result = value; error = failure; status = next;
                pending = continuations; continuations = null;
            }
            if (pending != null) foreach (var continuation in pending) SpeechAsync.Post(continuation);
            return true;
        }

        public UniTaskStatus GetStatus(short token) { lock (sync) return status; }
        public UniTaskStatus UnsafeGetStatus() => GetStatus(0);
        public T GetResult(short token)
        {
            lock (sync)
            {
                if (status == UniTaskStatus.Pending) throw new InvalidOperationException("The operation is still pending.");
                error?.Throw();
                return result;
            }
        }
        void IUniTaskSource.GetResult(short token) => GetResult(token);
        public void OnCompleted(Action<object> continuation, object state, short token)
        {
            if (continuation == null) throw new ArgumentNullException(nameof(continuation));
            Action resume = () => continuation(state);
            lock (sync)
            {
                if (status == UniTaskStatus.Pending)
                {
                    if (continuations == null) continuations = new List<Action>();
                    continuations.Add(resume);
                    return;
                }
            }
            SpeechAsync.Post(resume);
        }
    }
}

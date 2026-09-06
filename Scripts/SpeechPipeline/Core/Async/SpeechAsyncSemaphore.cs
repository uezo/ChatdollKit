using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.Async
{
    /// <summary>A FIFO asynchronous gate. Waiting never blocks a thread or requires a thread pool.</summary>
    public sealed class SpeechAsyncSemaphore : IDisposable
    {
        private sealed class Waiter
        {
            internal readonly SpeechCompletionSource<bool> Completion = new SpeechCompletionSource<bool>();
            internal LinkedListNode<Waiter> Node;
            internal CancellationTokenRegistration Registration;
            internal bool Finished;
        }
        private readonly object sync = new object();
        private readonly LinkedList<Waiter> waiters = new LinkedList<Waiter>();
        private readonly int maximum;
        private int count;
        private bool disposed;
        public SpeechAsyncSemaphore(int initialCount, int maxCount = int.MaxValue)
        {
            if (initialCount < 0 || maxCount < 1 || initialCount > maxCount) throw new ArgumentOutOfRangeException(nameof(initialCount));
            count = initialCount; maximum = maxCount;
        }
        public int CurrentCount { get { lock (sync) return count; } }
        public UniTask WaitAsync(CancellationToken cancellationToken = default)
        {
            Waiter waiter;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(SpeechAsyncSemaphore));
                cancellationToken.ThrowIfCancellationRequested();
                if (count > 0) { count--; return UniTask.CompletedTask; }
                waiter = new Waiter(); waiter.Node = waiters.AddLast(waiter);
            }
            if (cancellationToken.CanBeCanceled)
            {
                // Registration may invoke cancellation synchronously, or a release may win before
                // registration finishes. Publish the registration only while the waiter is pending.
                var registration = cancellationToken.Register(() => Cancel(waiter, cancellationToken));
                bool finished;
                lock (sync)
                {
                    finished = waiter.Finished;
                    if (!finished) waiter.Registration = registration;
                }
                if (finished) registration.Dispose();
            }
            return waiter.Completion.Task.AsUniTask();
        }
        private void Cancel(Waiter waiter, CancellationToken token)
        {
            CancellationTokenRegistration registration;
            lock (sync)
            {
                if (waiter.Finished) return;
                waiter.Finished = true;
                waiters.Remove(waiter.Node);
                registration = waiter.Registration;
                waiter.Registration = default;
            }
            waiter.Completion.TrySetCanceled(token);
            registration.Dispose();
        }
        public int Release()
        {
            Waiter waiter = null;
            int previous;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(SpeechAsyncSemaphore));
                previous = count;
                if (waiters.Count > 0)
                {
                    waiter = waiters.First.Value; waiters.RemoveFirst(); waiter.Finished = true;
                }
                else
                {
                    if (count == maximum) throw new InvalidOperationException("The asynchronous gate is already full.");
                    count++;
                }
            }
            waiter?.Completion.TrySetResult(true);
            waiter?.Registration.Dispose();
            return previous;
        }
        public void Dispose()
        {
            Waiter[] pending;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                pending = new Waiter[waiters.Count]; waiters.CopyTo(pending, 0); waiters.Clear();
                foreach (var waiter in pending)
                    waiter.Finished = true;
            }
            foreach (var waiter in pending)
            {
                waiter.Completion.TrySetException(new ObjectDisposedException(nameof(SpeechAsyncSemaphore)));
                waiter.Registration.Dispose();
            }
        }
    }
}

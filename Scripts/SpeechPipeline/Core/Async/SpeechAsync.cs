using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.Async
{
    /// <summary>Scheduling boundary for Unity's PlayerLoop and deterministic standalone tests.</summary>
    public interface ISpeechAsyncScheduler
    {
        void Post(Action continuation);
        UniTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public static class SpeechAsync
    {
#if UNITY_2018_3_OR_NEWER
        private static ISpeechAsyncScheduler scheduler = new PlayerLoopScheduler();
#else
        private static ISpeechAsyncScheduler scheduler;
#endif
        /// <summary>Configure once before creating services when running outside Unity.</summary>
        public static ISpeechAsyncScheduler Scheduler
        {
            get => scheduler ?? throw new InvalidOperationException("Configure a speech async scheduler before using standalone speech services.");
            set => scheduler = value ?? throw new ArgumentNullException(nameof(value));
        }
        public static void Post(Action continuation) => Scheduler.Post(continuation);
        public static UniTask Yield()
        {
            var completion = new SpeechCompletionSource<bool>();
            Post(() => completion.TrySetResult(true));
            return completion.Task.AsUniTask();
        }
        public static UniTask Delay(TimeSpan delay, CancellationToken cancellationToken = default) =>
            delay == System.Threading.Timeout.InfiniteTimeSpan ? UniTask.Never(cancellationToken) : Scheduler.DelayAsync(delay, cancellationToken);
        // Only native .NET transports and file APIs cross this boundary. Their completions
        // resume speech processing through the scheduler instead of on an I/O worker.
        public static UniTask FromTask(System.Threading.Tasks.Task operation) => Share(operation.AsUniTask(false));
        public static UniTask<T> FromTask<T>(System.Threading.Tasks.Task<T> operation) => Share(operation.AsUniTask(false));
        public static UniTask Run(Func<UniTask> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            var completion = new SpeechCompletionSource<bool>();
            Post(() => CompleteAsync(operation, completion).Forget());
            return completion.Task.AsUniTask();
        }
        public static UniTask<T> Run<T>(Func<UniTask<T>> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            var completion = new SpeechCompletionSource<T>();
            Post(() => CompleteAsync(operation, completion).Forget());
            return completion.Task;
        }
        public static UniTask Share(UniTask operation)
        {
            var completion = new SpeechCompletionSource<bool>();
            CompleteAsync(() => operation, completion).Forget();
            return completion.Task.AsUniTask();
        }
        public static UniTask<T> Share<T>(UniTask<T> operation)
        {
            var completion = new SpeechCompletionSource<T>();
            CompleteAsync(() => operation, completion).Forget();
            return completion.Task;
        }
        private static async UniTask CompleteAsync(Func<UniTask> operation, SpeechCompletionSource<bool> completion)
        {
            try { await operation(); completion.TrySetResult(true); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        private static async UniTask CompleteAsync<T>(Func<UniTask<T>> operation, SpeechCompletionSource<T> completion)
        {
            try { completion.TrySetResult(await operation()); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        public static UniTask WaitAsync(UniTask operation, CancellationToken cancellationToken) => operation.AttachExternalCancellation(cancellationToken);
        public static UniTask<T> WaitAsync<T>(UniTask<T> operation, CancellationToken cancellationToken) => operation.AttachExternalCancellation(cancellationToken);
        public static IDisposable Timeout(CancellationTokenSource source, TimeSpan delay) => new Deadline(source, delay);

        /// <summary>Wait for every already-started operation, including when one fails or is canceled.</summary>
        public static async UniTask WhenAll(IEnumerable<UniTask> operations)
        {
            var pending = new List<UniTask>(operations);
            List<Exception> failures = null;
            foreach (var operation in pending)
            {
                try { await operation; }
                catch (Exception error) { (failures ??= new List<Exception>()).Add(error); }
            }
            if (failures?.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures != null) throw new AggregateException(failures);
        }

        private sealed class Deadline : IDisposable
        {
            private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
            private int disposed;
            public Deadline(CancellationTokenSource target, TimeSpan delay) { WaitAsync(target, delay).Forget(); }
            private async UniTask WaitAsync(CancellationTokenSource target, TimeSpan delay)
            {
                try
                {
                    await Delay(delay, lifetime.Token);
                    // A completed delay may already have queued its continuation when the
                    // operation disposes its deadline. Do not cancel a later operation.
                    if (Volatile.Read(ref disposed) == 0) target.Cancel();
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                lifetime.Cancel(); lifetime.Dispose();
            }
        }
#if UNITY_2018_3_OR_NEWER
        private sealed class PlayerLoopScheduler : ISpeechAsyncScheduler
        {
            public void Post(Action continuation) => PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, continuation);
            public UniTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
                UniTask.Delay(delay, ignoreTimeScale: true, cancellationToken: cancellationToken, cancelImmediately: true);
        }
#endif
    }
}

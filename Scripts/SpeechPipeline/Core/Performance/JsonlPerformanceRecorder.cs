using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using Newtonsoft.Json;

namespace ChatdollKit.SpeechPipeline.Performance
{
    /// <summary>Appends one complete JSON record per line using an owned file stream.
    /// Use one recorder per path; concurrent readers are allowed, concurrent writers are not.</summary>
    /// <remarks>Cancellation is honored while waiting and before writing a line. Once a line starts writing,
    /// it is flushed to completion, including during disposal, so cancellation cannot leave a partial JSON line.
    /// I/O failures propagate to the caller. A filesystem failure can still leave a partial final line.</remarks>
    public sealed class JsonlPerformanceRecorder : IPipelinePerformanceRecorder
    {
        private readonly string path;
        private readonly object sync = new object();
        private readonly SpeechAsyncSemaphore operation = new SpeechAsyncSemaphore(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<UniTask> pending = new HashSet<UniTask>();
        private readonly SpeechCallbackGuard callbacks = new SpeechCallbackGuard();
        private FileStream stream;
        private bool disposing;
        private UniTask? disposeTask;

        public JsonlPerformanceRecorder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A JSONL file path is required.", nameof(path));
            this.path = Path.GetFullPath(path);
        }

        public UniTask RecordAsync(PipelinePerformanceRecord record, CancellationToken cancellationToken = default)
        {
            RejectReentry();
            var snapshot = record?.Copy() ?? throw new ArgumentNullException(nameof(record));
            var completion = new SpeechCompletionSource<bool>();
            var activeTask = completion.Task.AsUniTask();
            lock (sync)
            {
                if (disposing) throw new ObjectDisposedException(nameof(JsonlPerformanceRecorder));
                cancellationToken.ThrowIfCancellationRequested();
                pending.Add(activeTask);
            }
            _ = AppendAsync(snapshot, cancellationToken, completion, activeTask);
            return completion.Task;
        }

        private async UniTask AppendAsync(PipelinePerformanceRecord record, CancellationToken callerToken, SpeechCompletionSource<bool> completion, UniTask activeTask)
        {

            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, lifetime.Token))
                {
                    await operation.WaitAsync(linked.Token);
                    try
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(record, Formatting.None) + "\n");
                        if (stream == null)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, true);
                        }
                        linked.Token.ThrowIfCancellationRequested();
                        await SpeechAsync.FromTask(stream.WriteAsync(bytes, 0, bytes.Length, CancellationToken.None));
                        await SpeechAsync.FromTask(stream.FlushAsync(CancellationToken.None));
                    }
                    finally { operation.Release(); }
                    completion.TrySetResult(true);
                }
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { lock (sync) pending.Remove(activeTask); }
        }

        public UniTask DisposeAsync()
        {
            RejectReentry();
            UniTask[] active;
            SpeechCompletionSource<bool> completion;
            lock (sync)
            {
                if (disposeTask != null) return disposeTask.Value;
                disposing = true;
                active = pending.ToArray();
                completion = new SpeechCompletionSource<bool>();
                disposeTask = completion.Task;
            }
            _ = DisposeCoreAsync(active, completion);
            return disposeTask.Value;
        }

        private async UniTask DisposeCoreAsync(UniTask[] active, SpeechCompletionSource<bool> completion)
        {
            Exception failure = null;
            try { lifetime.Cancel(); } catch (Exception error) { failure = error; }
            try { await SpeechAsync.WhenAll(active); } catch { /* Each record task retains its own failure. */ }
            try { if (stream != null) await SpeechAsync.FromTask(stream.DisposeAsync().AsTask()); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            finally { lifetime.Dispose(); operation.Dispose(); }
            if (failure == null) completion.TrySetResult(true); else completion.TrySetException(failure);
        }

        private void RejectReentry()
        {
            callbacks.ThrowIfActive("Schedule recording or disposal after the current recorder operation returns.");
        }
    }
}

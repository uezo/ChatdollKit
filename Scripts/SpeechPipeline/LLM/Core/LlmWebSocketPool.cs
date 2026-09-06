using ChatdollKit.SpeechPipeline.Remote;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;

namespace ChatdollKit.SpeechPipeline.LLM
{
    // A lease is reusable only after its response has reached a successful terminal event.
    // Retired pools finish already admitted work but never retain another idle connection.
    internal sealed class LlmWebSocketPool : IDisposable
    {
        private readonly object sync = new object();
        private readonly Queue<Entry> idle = new Queue<Entry>();
        private readonly SpeechAsyncSemaphore capacity;
        private readonly Func<ILlmWebSocketConnection> factory;
        private readonly Uri uri;
        private readonly string apiKey;
        private readonly int maxConnections;
        private readonly double maxAgeSeconds;
        private bool retired;
        private bool disposed;

        internal LlmWebSocketPool(Uri uri, string apiKey, int maxConnections, double maxAgeSeconds,
            Func<ILlmWebSocketConnection> factory)
        {
            this.uri = uri;
            this.apiKey = apiKey;
            this.maxConnections = maxConnections;
            this.maxAgeSeconds = maxAgeSeconds;
            this.factory = factory;
            capacity = new SpeechAsyncSemaphore(maxConnections, maxConnections);
        }

        internal bool Matches(Uri candidateUri, string candidateKey, int candidateMaxConnections, double candidateMaxAge)
            => uri == candidateUri && apiKey == candidateKey && maxConnections == candidateMaxConnections && maxAgeSeconds == candidateMaxAge;

        internal async UniTask<Lease> RentAsync(CancellationToken cancellationToken)
        {
            await capacity.WaitAsync(cancellationToken);
            Entry entry = null;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (sync)
                    {
                        if (disposed) throw new ObjectDisposedException(nameof(LlmWebSocketPool));
                        entry = idle.Count == 0 ? null : idle.Dequeue();
                    }
                    if (entry == null || IsReusable(entry)) break;
                    DisposeConnection(entry.Connection);
                    entry = null;
                }
                if (entry == null)
                {
                    var connection = factory() ?? throw new InvalidOperationException("The WebSocket factory returned null.");
                    entry = new Entry(connection);
                    await connection.ConnectAsync(uri, apiKey, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new Lease(this, entry);
            }
            catch
            {
                if (entry != null) DisposeConnection(entry.Connection);
                capacity.Release();
                throw;
            }
        }

        internal void Retire()
        {
            Entry[] entries;
            lock (sync)
            {
                retired = true;
                entries = idle.ToArray();
                idle.Clear();
            }
            foreach (var entry in entries) DisposeConnection(entry.Connection);
        }

        private bool IsReusable(Entry entry)
        {
            try { return entry.Connection.IsOpen && (Stopwatch.GetTimestamp() - entry.CreatedAt) / (double)Stopwatch.Frequency < maxAgeSeconds; }
            catch { return false; }
        }

        private void Return(Entry entry, bool completed)
        {
            var reuse = completed && IsReusable(entry);
            lock (sync)
            {
                reuse &= !retired && !disposed;
                if (reuse) idle.Enqueue(entry);
            }
            try { if (!reuse) DisposeConnection(entry.Connection); }
            finally { capacity.Release(); }
        }

        // Called after the service has cancelled and awaited all request tasks.
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
            }
            Retire();
            capacity.Dispose();
        }

        private static void DisposeConnection(ILlmWebSocketConnection connection)
        {
            try { connection.Abort(); } catch { }
            try { connection.Dispose(); } catch { }
        }

        internal sealed class Entry
        {
            internal readonly ILlmWebSocketConnection Connection;
            internal readonly long CreatedAt = Stopwatch.GetTimestamp();
            internal Entry(ILlmWebSocketConnection connection) { Connection = connection; }
        }

        internal sealed class Lease : IDisposable
        {
            private readonly LlmWebSocketPool pool;
            private readonly Entry entry;
            private int returned;
            private bool completed;
            internal ILlmWebSocketConnection Connection => entry.Connection;
            internal Lease(LlmWebSocketPool pool, Entry entry) { this.pool = pool; this.entry = entry; }
            internal void MarkCompleted() { completed = true; }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref returned, 1) == 0) pool.Return(entry, completed);
            }
        }
    }
}

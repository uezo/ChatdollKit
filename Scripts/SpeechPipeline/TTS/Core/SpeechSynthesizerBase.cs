// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS
{
    /// <summary>One synthesizer processes one request at a time. Injected processors remain caller-owned.</summary>
    public abstract class SpeechSynthesizerBase : ISpeechSynthesizer
    {
        private readonly object sync = new object();
        private readonly SpeechAsyncSemaphore operation = new SpeechAsyncSemaphore(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<UniTask> pending = new HashSet<UniTask>();
        private readonly SpeechCallbackGuard callbacks = new SpeechCallbackGuard();
        private SpeechSynthesizerOptions options;
        private UniTask? disposeTask;
        private bool disposing;

        protected SpeechSynthesizerBase(SpeechSynthesizerOptions options) { this.options = Snapshot(options); }
        public SpeechSynthesizerOptions GetOptions() { lock (sync) return options.Copy(); }
        public void UpdateOptions(SpeechSynthesizerOptions replacement)
        {
            var copy = Snapshot(replacement);
            lock (sync)
            {
                ThrowIfDisposing();
                if (copy.GetType() != options.GetType()) throw new ArgumentException("Options must match this synthesizer's configuration type.");
                options = copy;
            }
        }

        public UniTask<byte[]> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
        {
            var copy = request?.Copy() ?? throw new ArgumentNullException(nameof(request));
            return RunOperationAsync((settings, token) => SynthesizeCoreAsync(copy, settings, token), cancellationToken);
        }

        /// <summary>Only runs provider generation, bypassing empty-text filtering, processors, resampling, and cache.</summary>
        public UniTask<byte[]> GenerateAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
        {
            var copy = request?.Copy() ?? throw new ArgumentNullException(nameof(request));
            return RunOperationAsync(async (settings, token) => CloneAudio(await callbacks.InvokeAsync(() => GenerateCoreAsync(copy, settings, token))), cancellationToken);
        }

        protected abstract UniTask<byte[]> GenerateCoreAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token);
        protected virtual UniTask DisposeResourcesAsync() => UniTask.CompletedTask;

        private async UniTask<byte[]> SynthesizeCoreAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions settings, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(request.Text)) return Array.Empty<byte>();
            foreach (var processor in settings.Preprocessors)
            {
                request.Text = await callbacks.InvokeAsync(() => processor.ProcessAsync(request.Copy(), settings.Copy(), token));
                token.ThrowIfCancellationRequested();
            }
            if (string.IsNullOrWhiteSpace(request.Text)) return Array.Empty<byte>();
            string key = null;
            if (!string.IsNullOrEmpty(settings.CacheDirectory))
            {
                key = await callbacks.InvokeAsync(() => MakeSynthesisCacheKeyAsync(request, settings, token));
                token.ThrowIfCancellationRequested();
                if (key != null)
                {
                    var cached = await ReadCacheAsync(settings, key, token);
                    if (cached != null && cached.Length != 0) return cached;
                }
            }
            var audio = CloneAudio(await callbacks.InvokeAsync(() => GenerateCoreAsync(request, settings, token)));
            token.ThrowIfCancellationRequested();
            if (audio.Length != 0)
            {
                foreach (var processor in settings.Postprocessors)
                {
                    audio = CloneAudio(await callbacks.InvokeAsync(() => processor.ProcessAsync(audio, settings.Copy(), token)));
                    token.ThrowIfCancellationRequested();
                }
                if (settings.SampleRate.HasValue && audio.Length != 0)
                    audio = WavResampler.ResampleIfWave(audio, settings.SampleRate.Value, token);
            }
            if (key != null && audio.Length != 0) await WriteCacheAsync(settings, key, audio, token);
            token.ThrowIfCancellationRequested();
            return audio;
        }

        protected virtual UniTask<string> MakeSynthesisCacheKeyAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions settings, CancellationToken token)
            => UniTask.FromResult(CreateCacheKey(request, settings));

        protected string CreateCacheKey(SpeechSynthesisRequest request, SpeechSynthesizerOptions settings, JToken extra = null)
        {
            var processors = new JArray();
            foreach (var processor in settings.Postprocessors)
            {
                var config = callbacks.Invoke(() => processor.GetCacheConfiguration(settings.Copy()));
                if (config == null) return null;
                processors.Add(new JObject { ["type"] = processor.GetType().FullName, ["config"] = config.DeepClone() });
            }
            var data = new JObject
            {
                ["type"] = GetType().FullName, ["request"] = JObject.FromObject(request),
                ["options"] = settings.GetCacheConfiguration(), ["processors"] = processors, ["extra"] = extra?.DeepClone()
            };
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(data.ToString(Formatting.None)))).Replace("-", "").ToLowerInvariant();
        }

        protected static string ParseStyle(JObject styleInfo, SpeechSynthesizerOptions settings)
        {
            var text = styleInfo?["styled_text"]?.Type == JTokenType.String ? (string)styleInfo["styled_text"] : string.Empty;
            foreach (var pair in settings.StyleMapper)
                if (text.IndexOf(pair.Key, StringComparison.Ordinal) >= 0) return pair.Value;
            return null;
        }

        protected UniTask<T> RunOperationAsync<T>(Func<SpeechSynthesizerOptions, CancellationToken, UniTask<T>> action, CancellationToken cancellationToken = default)
        {
            RejectReentry();
            if (action == null) throw new ArgumentNullException(nameof(action));
            SpeechSynthesizerOptions settings;
            var completion = new SpeechCompletionSource<T>();
            var activeTask = completion.Task.AsUniTask();
            lock (sync)
            {
                ThrowIfDisposing();
                cancellationToken.ThrowIfCancellationRequested();
                settings = options.Copy();
                pending.Add(activeTask);
            }
            _ = RunCoreAsync(action, settings, cancellationToken, completion, activeTask);
            return completion.Task;
        }

        private async UniTask RunCoreAsync<T>(Func<SpeechSynthesizerOptions, CancellationToken, UniTask<T>> action,
            SpeechSynthesizerOptions settings, CancellationToken cancellationToken, SpeechCompletionSource<T> completion, UniTask activeTask)
        {

            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken))
                {
                    using var timeout = SpeechAsync.Timeout(linked, TimeSpan.FromSeconds(settings.TimeoutSeconds));
                    await operation.WaitAsync(linked.Token);
                    T result;
                    try
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        result = await action(settings, linked.Token);
                        linked.Token.ThrowIfCancellationRequested();
                    }
                    finally { operation.Release(); }
                    completion.TrySetResult(result);
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
            try { await SpeechAsync.WhenAll(active); } catch { }
            try { await DisposeResourcesAsync(); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            finally { lifetime.Dispose(); operation.Dispose(); }
            if (failure == null) completion.TrySetResult(true); else completion.TrySetException(failure);
        }

        private static string CachePath(SpeechSynthesizerOptions settings, string key)
        {
            if (key.Length != 64 || key.Any(c => !(c >= 'a' && c <= 'f') && !(c >= '0' && c <= '9')))
                throw new InvalidOperationException("Cache keys must be lowercase SHA-256 hex strings.");
            return Path.Combine(settings.CacheDirectory, key + "." + settings.CacheExtension);
        }
        private static async UniTask<byte[]> ReadCacheAsync(SpeechSynthesizerOptions settings, string key, CancellationToken token)
        {
            var path = CachePath(settings, key);
            if (!File.Exists(path)) return null;
            try
            {
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                using (var output = new MemoryStream())
                {
                    await SpeechAsync.FromTask(input.CopyToAsync(output, 81920, token));
                    return output.ToArray();
                }
            }
            catch (FileNotFoundException) { return null; }
        }
        private static async UniTask WriteCacheAsync(SpeechSynthesizerOptions settings, string key, byte[] audio, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var path = CachePath(settings, key);
            Directory.CreateDirectory(settings.CacheDirectory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                    await SpeechAsync.FromTask(output.WriteAsync(audio, 0, audio.Length, token));
                token.ThrowIfCancellationRequested();
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { /* Another instance already published this complete cache entry. */ }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static byte[] CloneAudio(byte[] value) => value == null ? Array.Empty<byte>() : (byte[])value.Clone();
        private void ThrowIfDisposing() { if (disposing) throw new ObjectDisposedException(GetType().Name); }
        private void RejectReentry()
        {
            callbacks.ThrowIfActive("Schedule synthesis or disposal after this synthesizer's processor or callback returns.");
        }
        private static SpeechSynthesizerOptions Snapshot(SpeechSynthesizerOptions value)
        {
            var copy = value?.Copy() ?? throw new ArgumentNullException(nameof(value));
            copy.Validate();
            return copy;
        }
    }
}

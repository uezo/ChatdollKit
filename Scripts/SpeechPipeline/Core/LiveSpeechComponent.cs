using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline
{
    /// <summary>Inspector settings and their live binding. The pipeline owns the generated service.
    /// Create, bind, apply, and unbind on the Unity main thread. OnValidate only marks a revision.</summary>
    public abstract class LiveSpeechComponent : MonoBehaviour
    {
        private sealed class Binding
        {
            public Func<Func<CancellationToken, UniTask>> Prepare;
            public string RestartKey;
            public int Revision, ExternalRevision;
            public readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
            public UniTask Applying;
            public Action Clear;
        }
        internal sealed class SettingsStamp
        {
            internal int Revision, ExternalRevision;
            internal string RestartKey;
        }
        private int revision;
        private Binding binding;
        private UniTask unbinding = UniTask.CompletedTask;
        public bool IsBound => binding != null || !unbinding.Status.IsCompleted();
        public bool IsApplying => binding != null && !binding.Applying.Status.IsCompleted();
        public bool NeedsRestart { get; private set; }
        public string SettingsError { get; private set; }
        protected virtual int ExternalRevision => 0;
        public virtual string GetRestartKey() => string.Empty;
        public void NotifyChanged() => Interlocked.Increment(ref revision);
        protected virtual void OnValidate() => NotifyChanged();
        protected virtual async void OnDestroy()
        {
            try { await UnbindSettingsAsync(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        protected virtual void Update()
        {
            var current = binding;
            if (current != null && (current.Revision != revision || current.ExternalRevision != ExternalRevision))
                StartApplying(current);
        }

        internal SettingsStamp CaptureSettingsStamp() => new SettingsStamp
        { Revision = revision, ExternalRevision = ExternalRevision, RestartKey = GetRestartKey() };

        internal void BindSettings(Func<Func<CancellationToken, UniTask>> prepare)
            => BindSettingsSnapshot(prepare, CaptureSettingsStamp());

        internal void BindSettingsSnapshot(Func<Func<CancellationToken, UniTask>> prepare, SettingsStamp stamp, Action clear = null)
        {
            if (IsBound) throw new InvalidOperationException(name + " is already used by a running or stopping pipeline.");
            binding = new Binding
            {
                Prepare = prepare, RestartKey = stamp.RestartKey,
                Revision = stamp.Revision, ExternalRevision = stamp.ExternalRevision, Clear = clear
            };
            NeedsRestart = false; SettingsError = null;
        }

        /// <summary>Use after editing fields from application code. Inspector edits are applied automatically.</summary>
        public UniTask ApplySettingsAsync()
        {
            NotifyChanged();
            return binding == null ? UniTask.CompletedTask : StartApplying(binding);
        }

        private UniTask StartApplying(Binding current)
        {
            if (!current.Applying.Status.IsCompleted()) return current.Applying;
            var completion = new SpeechCompletionSource<bool>();
            current.Applying = completion.Task;
            _ = ApplyCoreAsync(current, completion);
            return completion.Task;
        }

        private async UniTask ApplyCoreAsync(Binding current, SpeechCompletionSource<bool> completion)
        {
            try
            {
                while (ReferenceEquals(binding, current) && !current.Lifetime.IsCancellationRequested)
                {
                    var nextRevision = revision;
                    var nextExternal = ExternalRevision;
                    if (current.Revision == nextRevision && current.ExternalRevision == nextExternal) break;
                    try
                    {
                        if (current.RestartKey != GetRestartKey())
                        {
                            NeedsRestart = true;
                            SettingsError = "This change requires Restart Pipeline. The running configuration is unchanged.";
                        }
                        else
                        {
                            // Capture Unity fields now. The asynchronous operation only receives this snapshot.
                            var apply = current.Prepare();
                            await apply(current.Lifetime.Token);
                            if (!ReferenceEquals(binding, current)) break;
                            NeedsRestart = false; SettingsError = null;
                        }
                    }
                    catch (OperationCanceledException) when (current.Lifetime.IsCancellationRequested) { break; }
                    catch (Exception error)
                    {
                        if (!ReferenceEquals(binding, current)) break;
                        SettingsError = error.Message;
                    }
                    current.Revision = nextRevision; current.ExternalRevision = nextExternal;
                }
                completion.TrySetResult(true);
            }
            catch (Exception error) { SettingsError = error.Message; completion.TrySetResult(true); }
        }

        internal UniTask UnbindSettingsAsync()
        {
            var previous = binding;
            binding = null;
            if (previous == null) return unbinding;
            previous.Clear?.Invoke();
            Exception failure = null;
            try { previous.Lifetime.Cancel(); } catch (Exception error) { failure = error; }
            return unbinding = SpeechAsync.Share(FinishUnbindAsync(previous, failure));
        }
        private static async UniTask FinishUnbindAsync(Binding previous, Exception failure)
        {
            try { await previous.Applying; }
            finally { previous.Lifetime.Dispose(); }
            if (failure != null) throw failure;
        }
    }
}

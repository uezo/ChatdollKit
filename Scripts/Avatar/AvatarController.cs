using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.Model;
using UnityEngine;

namespace ChatdollKit.Avatar
{
    /// <summary>Unity presentation only. Conversation state, response queueing and input policy belong to the orchestrator.</summary>
    [DisallowMultipleComponent, AddComponentMenu("ChatdollKit/Avatar/Avatar Controller")]
    public sealed class AvatarController : MonoBehaviour, IAvatarController
    {
        public ModelController ModelController;
        public FaceController FaceController;
        public SpeechController SpeechController;
        [Min(0)] public float DefaultAnimationDurationSeconds = 4;
        public bool ResetFaceOnStop = true;
        public bool StartIdlingOnStop = true;
        /// <summary>Called on the main thread when the prepared presentation begins, including silent controls.</summary>
        public event Action<AvatarRequest> PresentationStarted;

        private sealed class PresentationWork
        {
            public long Sequence;
            public AvatarRequest Presentation;
            public CancellationTokenSource Source;
            public SpeechCompletionSource<bool> Completion = new SpeechCompletionSource<bool>();
        }
        private readonly object sync = new object();
        private readonly HashSet<PresentationWork> pending = new HashSet<PresentationWork>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private long sequence, lastStop;
        private bool destroyed;

        private void Awake() => ResolveComponents();
        private void OnDisable() { Observe(StopAsync()); }
        private void OnDestroy() { Observe(CloseAsync()); }

        public UniTask PresentAsync(AvatarRequest presentation, CancellationToken token)
        {
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));
            PresentationWork work;
            lock (sync)
            {
                if (destroyed) throw new ObjectDisposedException(nameof(AvatarController));
                token.ThrowIfCancellationRequested();
                work = new PresentationWork
                {
                    Sequence = ++sequence, Presentation = presentation.Copy(),
                    Source = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token)
                };
                pending.Add(work);
            }
            _ = PresentCoreAsync(work);
            return work.Completion.Task;
        }

        private async UniTask PresentCoreAsync(PresentationWork work)
        {
            Exception failure = null;
            var token = work.Source.Token;
            try
            {
                await UniTask.SwitchToMainThread();
                token.ThrowIfCancellationRequested();
                if (this == null || !isActiveAndEnabled) throw new InvalidOperationException("AvatarController must be enabled before presentation.");
                ResolveComponents();
                var audio = work.Presentation.AudioData;
                UniTask playback;
                lock (sync)
                {
                    if (work.Sequence <= lastStop) throw new OperationCanceledException(token);
                    token.ThrowIfCancellationRequested();
                    if (audio != null && audio.Length > 0)
                    {
                        if (SpeechController == null) throw new InvalidOperationException("Assign the new Avatar.SpeechController for audio playback.");
                        playback = SpeechController.PlayAsync(audio, () => BeginPresentation(work, token), token);
                    }
                    else { BeginPresentation(work, token); playback = UniTask.CompletedTask; }
                }
                await playback;
                token.ThrowIfCancellationRequested();
            }
            catch (Exception error) { failure = error; }
            finally
            {
                lock (sync) pending.Remove(work);
                work.Source.Dispose();
                if (failure is OperationCanceledException) work.Completion.TrySetCanceled();
                else if (failure != null) work.Completion.TrySetException(failure);
                else work.Completion.TrySetResult(true);
            }
        }

        private void BeginPresentation(PresentationWork work, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (sync) if (work.Sequence <= lastStop) throw new OperationCanceledException(token);
            var faces = new List<FaceExpression>();
            var animations = new List<ChatdollKit.Model.Animation>();
            foreach (var control in work.Presentation.Controls)
            {
                if (control.Kind == AvatarControlKind.Face && FaceController != null)
                    faces.Add(new FaceExpression(control.Name, (float)(control.DurationSeconds ?? FaceController.DefaultFaceExpressionDuration)));
                else if (control.Kind == AvatarControlKind.Animation && ModelController != null)
                {
                    if (ModelController.IsAnimationRegistered(control.Name))
                        animations.Add(ModelController.GetRegisteredAnimation(control.Name, (float)(control.DurationSeconds ?? DefaultAnimationDurationSeconds)));
                    else Debug.LogWarning("Animation is not registered: " + control.Name, this);
                }
            }
            if (faces.Count > 0) FaceController.SetFace(faces);
            if (animations.Count > 0) ModelController.Animate(animations);
            var handlers = PresentationStarted;
            if (handlers != null)
                foreach (Action<AvatarRequest> handler in handlers.GetInvocationList()) handler(work.Presentation.Copy());
        }

        public UniTask StopAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            PresentationWork[] snapshot;
            long stopSequence;
            UniTask stopAudio;
            lock (sync)
            {
                stopSequence = lastStop = ++sequence;
                snapshot = pending.ToArray();
                // Registration under the same lock prevents this stop from capturing a later presentation's audio.
                stopAudio = !ReferenceEquals(SpeechController, null) ? SpeechController.StopAsync() : UniTask.CompletedTask;
            }
            foreach (var work in snapshot)
                try { work.Source.Cancel(); } catch (ObjectDisposedException) { }
            return StopCoreAsync(snapshot, stopAudio, stopSequence);
        }

        private async UniTask StopCoreAsync(PresentationWork[] snapshot, UniTask stopAudio, long stopSequence)
        {
            await UniTask.SwitchToMainThread();
            lock (sync)
            {
                if (this != null && sequence == stopSequence)
                {
                    ResolveComponents();
                    if (ResetFaceOnStop && FaceController != null) FaceController.SetFace(new List<FaceExpression> { new FaceExpression("Neutral") });
                    if (StartIdlingOnStop && ModelController != null) ModelController.StartIdling();
                }
            }
            var failures = new List<Exception>();
            foreach (var operation in snapshot.Select(work => (UniTask)work.Completion.Task).Append(stopAudio))
            {
                try { await operation; }
                catch (OperationCanceledException) { }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0) throw new AggregateException(failures);
        }

        /// <summary>Fill missing references without replacing components explicitly selected by the application.</summary>
        public void ResolveComponents()
        {
            if (ModelController == null) ModelController = GetComponent<ModelController>();
            if (FaceController == null) FaceController = GetComponent<FaceController>();
            if (FaceController == null && ModelController != null) FaceController = ModelController.FaceController;
            if (SpeechController == null) SpeechController = GetComponent<SpeechController>();
        }
        private async UniTask CloseAsync()
        {
            lock (sync) { if (destroyed) return; destroyed = true; }
            try { lifetime.Cancel(); } catch (Exception error) { Debug.LogException(error); }
            try { await StopAsync(); }
            finally { lifetime.Dispose(); }
        }
        private static async void Observe(UniTask task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception error) { await UniTask.SwitchToMainThread(); Debug.LogException(error); }
        }
    }
}

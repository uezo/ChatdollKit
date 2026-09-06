using ChatdollKit.Avatar.LipSync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.Model;
using UnityEngine;
using UnityEngine.Serialization;

namespace ChatdollKit.Avatar
{
    /// <summary>Plays complete PCM WAV files on a dedicated AudioSource. It never invokes a TTS service.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(AudioSource)), AddComponentMenu("ChatdollKit/Avatar/Speech Controller")]
    public sealed class SpeechController : MonoBehaviour
    {
        public AudioSource AudioSource;
        [Tooltip("Optional ILipSyncHelper for resetting mouth shapes when playback ends. If empty, use a helper on this GameObject.")]
        public MonoBehaviour LipSyncHelper;
        [SerializeField, FormerlySerializedAs("speechLipSyncComponent")]
        [Tooltip("Optional lip sync engine, such as MfccLipSync. Leave empty when using uLipSync.")]
        private MonoBehaviour lipSyncEngineComponent;
        private ILipSync lipSyncEngineOverride;
        /// <summary>Explicit PCM lip sync input. Unity serializes the component; code may also inject a plain interface implementation.</summary>
        public ILipSync LipSyncEngine
        {
            get => lipSyncEngineOverride ?? lipSyncEngineComponent as ILipSync;
            set
            {
                lipSyncEngineComponent = value as MonoBehaviour;
                lipSyncEngineOverride = value is MonoBehaviour ? null : value;
            }
        }
        [Min(1)] public int LipSyncFramesPerMessage = 512;
        /// <summary>Played, interleaved samples with channels and sample rate. Called on Unity's main thread.</summary>
        public event Action<float[], int, int> PlayingSamples;
        public bool IsPlaying { get { lock (sync) return active != null; } }

        private sealed class Playback
        {
            public long Generation;
            public CancellationTokenSource Source;
            public SpeechCompletionSource<bool> Completion = new SpeechCompletionSource<bool>();
        }
        private readonly object sync = new object();
        private readonly SpeechAsyncSemaphore playbackGate = new SpeechAsyncSemaphore(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<Playback> pending = new HashSet<Playback>();
        private Playback active;
        private long generation;
        private bool destroyed;

        private void Awake() { if (AudioSource == null) AudioSource = GetComponent<AudioSource>(); }
        private void OnDisable() { Observe(StopAsync()); }
        private void OnDestroy() { Observe(CloseAsync()); }

        public UniTask PlayAsync(byte[] wave, CancellationToken token = default) => PlayAsync(wave, null, token);

        // The avatar applies controls only after decoding, immediately before playback starts.
        internal UniTask PlayAsync(byte[] wave, Action beforePlayback, CancellationToken token)
        {
            var copy = (byte[])(wave ?? throw new ArgumentNullException(nameof(wave))).Clone();
            Playback work;
            lock (sync)
            {
                if (destroyed) throw new ObjectDisposedException(nameof(SpeechController));
                token.ThrowIfCancellationRequested();
                work = new Playback { Generation = generation, Source = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token) };
                pending.Add(work);
            }
            _ = RunPlaybackAsync(copy, beforePlayback, work);
            return work.Completion.Task;
        }

        private async UniTask RunPlaybackAsync(byte[] wave, Action beforePlayback, Playback work)
        {
            AudioClip clip = null;
            ILipSync lipSync = null;
            ILipSyncHelper lipSyncHelper = null;
            var entered = false;
            Exception failure = null;
            var token = work.Source.Token;
            try
            {
                await playbackGate.WaitAsync(token);
                entered = true;
                await UniTask.SwitchToMainThread();
                var decoded = WaveAudio.Decode(wave, token);
                token.ThrowIfCancellationRequested();
                if (this == null || !isActiveAndEnabled) throw new InvalidOperationException("SpeechController must be enabled before playback.");
                if (AudioSource == null) AudioSource = GetComponent<AudioSource>();
                if (AudioSource == null) throw new InvalidOperationException("Assign an AudioSource for playback.");
                if (!AudioSource.isActiveAndEnabled) throw new InvalidOperationException("The playback AudioSource must be enabled.");
                lock (sync) active = work;
                if (decoded.FrameCount > 0)
                {
                    clip = AudioClip.Create("Chatdoll speech", decoded.FrameCount, decoded.Channels, decoded.SampleRate, false);
                    if (!clip.SetData(decoded.Samples, 0)) throw new InvalidOperationException("Could not fill the speech AudioClip.");
                }
                token.ThrowIfCancellationRequested();
                beforePlayback?.Invoke();
                token.ThrowIfCancellationRequested();
                lipSyncHelper = LipSyncHelper != null
                    ? LipSyncHelper as ILipSyncHelper : GetComponent<ILipSyncHelper>();
                lipSync = LipSyncEngine;
                if (lipSync is Behaviour behaviour && (behaviour == null || !behaviour.isActiveAndEnabled)) lipSync = null;
                if (clip != null)
                {
                    AudioSource.Stop();
                    AudioSource.clip = clip;
                    AudioSource.loop = false;
                    lipSync?.BeginPlayback(decoded);
                    token.ThrowIfCancellationRequested();
                    AudioSource.Play();
                    var sent = 0;
                    while (true)
                    {
                        // OnDisable/OnDestroy cancels before the next player-loop tick.
                        // Check that cancellation before accessing a potentially destroyed AudioSource.
                        token.ThrowIfCancellationRequested();
                        if (!AudioSource.isPlaying) break;
                        var position = AudioSource.timeSamples;
                        // timeSamples is per-channel frames, unlike the interleaved sample notifications.
                        if (LipSyncTargetExists(lipSync)) lipSync.UpdatePlayback(position);
                        EmitSamples(decoded, ref sent, position * decoded.Channels, false, token);
                        await UniTask.Yield(PlayerLoopTiming.Update);
                    }
                    token.ThrowIfCancellationRequested();
                    EmitSamples(decoded, ref sent, decoded.Samples.Length, true, token);
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                if (entered)
                {
                    await UniTask.SwitchToMainThread();
                    try
                    {
                        bool ownsPlayback;
                        lock (sync) { ownsPlayback = ReferenceEquals(active, work); if (ownsPlayback) active = null; }
                        if (ownsPlayback)
                        {
                            if (AudioSource != null && AudioSource.clip == clip) { AudioSource.Stop(); AudioSource.clip = null; }
                            try
                            {
                                if (LipSyncTargetExists(lipSync)) lipSync.ResetViseme();
                            }
                            finally
                            {
                                if (LipSyncTargetExists(lipSyncHelper) && !ReferenceEquals(lipSyncHelper, lipSync))
                                    lipSyncHelper.ResetViseme();
                            }
                        }
                    }
                    catch (Exception error) { failure = failure ?? error; }
                    finally
                    {
                        if (clip != null) Destroy(clip);
                        playbackGate.Release();
                    }
                }
                lock (sync) pending.Remove(work);
                work.Source.Dispose();
                if (failure is OperationCanceledException) work.Completion.TrySetCanceled();
                else if (failure != null) work.Completion.TrySetException(failure);
                else work.Completion.TrySetResult(true);
            }
        }

        private static bool LipSyncTargetExists(object lipSync)
            => lipSync != null && (!(lipSync is UnityEngine.Object component) || component != null);

        private void EmitSamples(WaveAudio audio, ref int sent, int playedSamples, bool final, CancellationToken token)
        {
            var available = Math.Min(playedSamples, audio.Samples.Length);
            var batch = checked(Math.Max(1, LipSyncFramesPerMessage) * audio.Channels);
            if (PlayingSamples == null) { sent = available; return; }
            while (available - sent >= batch || (final && sent < available))
            {
                token.ThrowIfCancellationRequested();
                var count = Math.Min(batch, available - sent);
                var samples = new float[count];
                Array.Copy(audio.Samples, sent, samples, 0, count);
                PlayingSamples?.Invoke(samples, audio.Channels, audio.SampleRate);
                sent += count;
            }
        }

        public UniTask StopAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Playback[] snapshot;
            long cutoff;
            lock (sync) { cutoff = generation++; snapshot = pending.ToArray(); }
            foreach (var work in snapshot) Cancel(work);
            // Accepted stops finish cleanup even when the caller later cancels its token.
            return StopCoreAsync(snapshot, cutoff);
        }
        private async UniTask StopCoreAsync(Playback[] snapshot, long cutoff)
        {
            await UniTask.SwitchToMainThread();
            bool stop;
            lock (sync) stop = active != null && active.Generation <= cutoff;
            if (stop && AudioSource != null) AudioSource.Stop();
            var failures = new List<Exception>();
            foreach (var work in snapshot)
            {
                try { await work.Completion.Task; }
                catch (OperationCanceledException) { }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0) throw new AggregateException(failures);
        }
        private async UniTask CloseAsync()
        {
            lock (sync) { if (destroyed) return; destroyed = true; }
            try { lifetime.Cancel(); } catch (Exception error) { Debug.LogException(error); }
            try { await StopAsync(); }
            finally { lifetime.Dispose(); playbackGate.Dispose(); }
        }
        private static void Cancel(Playback work)
        { try { work.Source.Cancel(); } catch (ObjectDisposedException) { } }
        private static async void Observe(UniTask task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception error) { await UniTask.SwitchToMainThread(); Debug.LogException(error); }
        }
    }
}

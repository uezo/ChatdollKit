using ChatdollKit.Avatar.LipSync;
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline.STT;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using NewAvatar = ChatdollKit.Avatar.AvatarController;
using NewSpeech = ChatdollKit.Avatar.SpeechController;

namespace ChatdollKit.Tests.Avatar
{
    /// <summary>Play Mode tests. AudioSource volume is zero; all audio is local silence.</summary>
    public class AvatarControllerUnityTests
    {
        [UnityTest]
        public IEnumerator WorkerPlaybackAndSampleCallbacksUseMainThreadAndReleaseClip() => Run(async () =>
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var gameObject = Create(out var speech, out var avatar);
            AudioClip clip = null;
            var sampleCalls = 0;
            try
            {
                speech.AudioSource.loop = true;
                avatar.PresentationStarted += item =>
                {
                    Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                    item.TransactionId = "changed"; item.Text = "changed"; item.VoiceText = "changed";
                    item.AudioData[0] = 0;
                    item.Controls = new[] { new AvatarControl(AvatarControlKind.Face, "changed") };
                };
                avatar.PresentationStarted += item =>
                {
                    Assert.That(item.TransactionId, Is.EqualTo("natural"));
                    Assert.That(item.Text, Is.EqualTo("silent test"));
                    Assert.That(item.VoiceText, Is.EqualTo("silent test"));
                    Assert.That(item.AudioData[0], Is.EqualTo((byte)'R'));
                    Assert.That(item.Controls, Is.Empty);
                };
                speech.PlayingSamples += (samples, channels, rate) =>
                {
                    Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                    Assert.That(channels, Is.EqualTo(1)); Assert.That(rate, Is.EqualTo(16000));
                    Assert.That(speech.AudioSource.loop, Is.False);
                    clip = speech.AudioSource.clip;
                    sampleCalls++;
                };
                await Background(() => avatar.PresentAsync(Presentation("natural", 0.2), CancellationToken.None));
                Assert.That(sampleCalls, Is.GreaterThan(0));
                Assert.That(speech.AudioSource.clip, Is.Null);
                await SpeechAsync.Yield(); await SpeechAsync.Yield();
                Assert.That(clip == null, Is.True, "The generated AudioClip is destroyed after playback.");
            }
            finally { UnityEngine.Object.DestroyImmediate(gameObject); }
        });

        [UnityTest]
        public IEnumerator StopCannotCancelAPresentationRegisteredAfterIt() => Run(async () =>
        {
            var gameObject = Create(out var speech, out var avatar);
            var oldStarted = Signal(); var newStarted = Signal(); var stopRegistered = Signal();
            using (var newCancellation = new CancellationTokenSource())
            {
                avatar.PresentationStarted += item =>
                { if (item.TransactionId == "old") oldStarted.TrySetResult(true); else newStarted.TrySetResult(true); };
                try
                {
                    var oldPlay = Background(() => avatar.PresentAsync(Presentation("old", 5), CancellationToken.None));
                    await oldStarted.Task;
                    var stop = Background(() =>
                    {
                        var stopping = avatar.StopAsync();
                        stopRegistered.TrySetResult(true);
                        return stopping;
                    });
                    await stopRegistered.Task;
                    var newPlay = Background(() => avatar.PresentAsync(Presentation("new", 5), newCancellation.Token));
                    await newStarted.Task;
                    await stop;
                    Assert.That(newPlay.Status.IsCompleted(), Is.False);
                    Assert.That(speech.IsPlaying, Is.True);
                    await Canceled(oldPlay);
                    newCancellation.Cancel();
                    await Canceled(newPlay);
                    Assert.That(speech.AudioSource.clip, Is.Null);
                }
                finally { newCancellation.Cancel(); await avatar.StopAsync(); UnityEngine.Object.DestroyImmediate(gameObject); }
            }
        });

        [UnityTest]
        public IEnumerator DisableThenDestroyCancelsPlaybackAndReleasesClip() => Run(async () =>
        {
            var gameObject = Create(out var speech, out var avatar);
            var samplesArrived = Signal();
            AudioClip clip = null;
            speech.PlayingSamples += (samples, channels, rate) => { clip = speech.AudioSource.clip; samplesArrived.TrySetResult(true); };
            var play = Background(() => avatar.PresentAsync(Presentation("destroy", 5), CancellationToken.None));
            try
            {
                await samplesArrived.Task;
                avatar.enabled = false;
                UnityEngine.Object.DestroyImmediate(gameObject);
                await Canceled(play);
                await SpeechAsync.Yield(); await SpeechAsync.Yield();
                Assert.That(clip == null, Is.True);
            }
            finally { if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject); }
        });

        private static GameObject Create(out NewSpeech speech, out NewAvatar avatar)
        {
            var gameObject = new GameObject("Avatar presentation test");
            speech = gameObject.AddComponent<NewSpeech>();
            speech.AudioSource.volume = 0;
            speech.AudioSource.playOnAwake = false;
            avatar = gameObject.AddComponent<NewAvatar>();
            avatar.SpeechController = speech;
            return gameObject;
        }
        private static AvatarRequest Presentation(string transaction, double duration) => new AvatarRequest
        {
            TransactionId = transaction, Text = "silent test", VoiceText = "silent test",
            AudioData = Pcm16Audio.WriteWave(new byte[(int)(duration * 16000) * 2], 16000)
        };
        private static async UniTask Canceled(UniTask task)
        {
            try { await task; Assert.Fail("The presentation must be canceled."); }
            catch (OperationCanceledException) { }
        }
        private static SpeechCompletionSource<bool> Signal() => new SpeechCompletionSource<bool>();
        private static IEnumerator Run(Func<UniTask> action)
        {
            var task = action();
            var deadline = Time.realtimeSinceStartup + 10;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, "Playback lifecycle must not deadlock.");
            task.GetAwaiter().GetResult();
        }
        private static UniTask Background(Func<UniTask> operation)
            => SpeechAsync.Share(SpeechAsync.FromTask(NUnitTask.Run(async () => await operation())));

    }
}
#endif

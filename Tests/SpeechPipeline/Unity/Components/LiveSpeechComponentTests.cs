using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.VAD;
using NUnit.Framework;
using UnityEngine;

namespace ChatdollKit.Tests.SpeechPipeline.Unity
{
    public class LiveSpeechComponentTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<LiveSpeechComponent> components = new List<LiveSpeechComponent>();
        private readonly List<SpeechDetectorLease> leases = new List<SpeechDetectorLease>();

        [TearDown]
        public async NUnitTask Cleanup()
        {
            foreach (var component in components) await Unbind(component);
            foreach (var lease in leases) await lease.DisposeAsync();
            foreach (var item in objects) UnityEngine.Object.DestroyImmediate(item);
            components.Clear(); leases.Clear(); objects.Clear();
        }

        [Test]
        public async NUnitTask StandardComponentAppliesInspectorVolumeToExistingSession()
        {
            var component = Create<StandardSpeechDetector>();
            var lease = await CreateDetector(component);
            var supplied = (StandardSpeechDetectorOptions)component.BuildOptions();
            supplied.VolumeDbThreshold = 0;
            Assert.That(await lease.Detector.ProcessSamplesAsync(Pcm(100)), Is.False);
            BindDetector(component, lease.Detector);

            component.VolumeDbThreshold = -60;
            await component.ApplySettingsAsync();

            Assert.That(component.SettingsError, Is.Null);
            Assert.That(await lease.Detector.ProcessSamplesAsync(Pcm(100)), Is.True);
            Assert.That(((StandardSpeechDetectorOptions)lease.Detector.GetOptions()).VolumeDbThreshold, Is.EqualTo(-60));
        }

        [Test]
        public async NUnitTask StructuralChangeRequiresRestartAndRejectsTheEntireInspectorSnapshot()
        {
            var component = Create<StandardSpeechDetector>();
            var lease = await CreateDetector(component);
            BindDetector(component, lease.Detector);
            component.Settings.PrerollBufferCount++;
            component.VolumeDbThreshold = -60;
            await component.ApplySettingsAsync();

            Assert.That(component.NeedsRestart, Is.True);
            Assert.That(component.SettingsError, Does.Contain("Restart"));
            Assert.That(((StandardSpeechDetectorOptions)lease.Detector.GetOptions()).VolumeDbThreshold, Is.EqualTo(-40));
            Assert.That(lease.Detector.GetOptions().PrerollBufferCount, Is.EqualTo(5));

            component.Settings.PrerollBufferCount--;
            await component.ApplySettingsAsync();
            Assert.That(component.NeedsRestart, Is.False);
            Assert.That(component.SettingsError, Is.Null);
            Assert.That(((StandardSpeechDetectorOptions)lease.Detector.GetOptions()).VolumeDbThreshold, Is.EqualTo(-60));
        }

        [Test]
        public async NUnitTask InvalidInspectorSettingsLeaveTheRunningDetectorUsableAndCanBeCorrected()
        {
            var component = Create<StandardSpeechDetector>();
            var lease = await CreateDetector(component);
            BindDetector(component, lease.Detector);
            component.Settings.SilenceDurationThreshold = -1;
            component.VolumeDbThreshold = -60;
            await component.ApplySettingsAsync();
            Assert.That(component.SettingsError, Is.Not.Null.And.Not.Empty);
            Assert.That(((StandardSpeechDetectorOptions)lease.Detector.GetOptions()).VolumeDbThreshold, Is.EqualTo(-40));

            component.Settings.SilenceDurationThreshold = 0.25f;
            await component.ApplySettingsAsync();
            Assert.That(component.SettingsError, Is.Null);
            Assert.That(lease.Detector.GetOptions().SilenceDurationThreshold, Is.EqualTo(0.25));
            Assert.That(await lease.Detector.ProcessSamplesAsync(Pcm(100)), Is.True);
        }

        [Test]
        public async NUnitTask EditsDuringAnApplyUseDetachedSnapshotsAndApplyTheLatestRevision()
        {
            var component = Create<StandardSpeechDetector>();
            var release = Signal();
            var applied = new List<double>();
            Bind(component, () =>
            {
                var snapshot = (StandardSpeechDetectorOptions)component.BuildOptions();
                return async token =>
                {
                    if (applied.Count == 0) await release.Task;
                    token.ThrowIfCancellationRequested();
                    applied.Add(snapshot.VolumeDbThreshold);
                };
            });
            component.VolumeDbThreshold = -60;
            var first = component.ApplySettingsAsync();
            try
            {
                Assert.That(component.IsApplying, Is.True);
                component.VolumeDbThreshold = -70;
                var next = component.ApplySettingsAsync();
                component.VolumeDbThreshold = -80;
                var latest = component.ApplySettingsAsync();
                Assert.That(next, Is.EqualTo(first));
                Assert.That(latest, Is.EqualTo(first));
                release.TrySetResult(true);
                await Within(first);
                CollectionAssert.AreEqual(new[] { -60.0, -80.0 }, applied);
                Assert.That(component.IsApplying, Is.False);
            }
            finally { release.TrySetResult(true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask UnbindingReservesTheComponentUntilThePendingApplyFinishes(bool failLate)
        {
            var component = Create<StandardSpeechDetector>();
            var release = Signal();
            CancellationToken previousToken = default;
            Bind(component, () => async token =>
            {
                previousToken = token;
                await release.Task; // Deliberately model a provider that is slow to observe cancellation.
                if (failLate) throw new InvalidOperationException("old binding failed");
                token.ThrowIfCancellationRequested();
            });
            component.VolumeDbThreshold = -60;
            var applying = component.ApplySettingsAsync();
            var unbinding = Unbind(component);
            try
            {
                Assert.That(previousToken.IsCancellationRequested, Is.True);
                Assert.That(component.IsBound, Is.True, "The stopping pipeline retains ownership until its apply finishes.");
                Assert.That(unbinding.Status.IsCompleted(), Is.False);
                var applied = 0;
                var error = Assert.Throws<TargetInvocationException>(() => Bind(component, () => token => UniTask.CompletedTask));
                Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
                release.TrySetResult(true);
                await Within(UniTask.WhenAll(applying, unbinding));
                Assert.That(component.IsBound, Is.False);
                Assert.That(component.SettingsError, Is.Null);
                Bind(component, () => token => { applied++; return UniTask.CompletedTask; });
                await component.ApplySettingsAsync();
                Assert.That(component.IsBound, Is.True);
                Assert.That(applied, Is.EqualTo(1));
            }
            finally { release.TrySetResult(true); }
        }

        [Test]
        public async NUnitTask UnbindingCancelsAVolumeUpdateQueuedBehindAudioProcessing()
        {
            var component = Create<StandardSpeechDetector>();
            var lease = await CreateDetector(component);
            BindDetector(component, lease.Detector);
            var releaseAudio = Signal();
            lease.Detector.Voiced += id => releaseAudio.Task;
            var processing = lease.Detector.ProcessSamplesAsync(Pcm(1000));
            try
            {
                Assert.That(processing.Status.IsCompleted(), Is.False);
                component.VolumeDbThreshold = -60;
                var applying = component.ApplySettingsAsync();
                Assert.That(component.IsApplying, Is.True);
                await Within(Unbind(component));
                await Within(applying);
                Assert.That(((StandardSpeechDetectorOptions)lease.Detector.GetOptions()).VolumeDbThreshold, Is.EqualTo(-40));
                Assert.That(component.IsBound, Is.False);
            }
            finally
            {
                releaseAudio.TrySetResult(true);
                await Within(processing);
            }
        }

        [Test]
        public async NUnitTask OpenAIProvidersRefreshTheirOwnCredentialsIndependentlyWithoutRequests()
        {
            var stt = Create<OpenAISpeechRecognizer>();
            var llm = stt.gameObject.AddComponent<OpenAIResponsesService>(); components.Add(llm);
            var tts = stt.gameObject.AddComponent<OpenAISpeechSynthesizer>(); components.Add(tts);
            stt.ApiKey = "stt-key-one";
            llm.ApiKey = "llm-key-one";
            tts.ApiKey = "tts-key-one";
            stt.BaseUrl = llm.BaseUrl = tts.BaseUrl = "https://first.invalid/v1";
            var sttOptions = (OpenAISpeechRecognizerOptions)stt.BuildOptions();
            var llmOptions = llm.BuildOptions();
            var ttsOptions = (OpenAISpeechSynthesizerOptions)tts.BuildOptions();
            Bind(stt, () =>
            {
                var next = (OpenAISpeechRecognizerOptions)stt.BuildOptions(sttOptions);
                return token => { sttOptions = next; return UniTask.CompletedTask; };
            });
            Bind(llm, () =>
            {
                var next = llm.BuildOptions(llmOptions);
                return token => { llmOptions = next; return UniTask.CompletedTask; };
            });
            Bind(tts, () =>
            {
                var next = (OpenAISpeechSynthesizerOptions)tts.BuildOptions(ttsOptions);
                return token => { ttsOptions = next; return UniTask.CompletedTask; };
            });

            stt.ApiKey = "stt-key-two";
            stt.BaseUrl = "https://stt.invalid/v1";
            stt.NotifyChanged();
            Tick(stt); Tick(llm); Tick(tts);
            await SpeechAsync.Yield();
            Assert.That(sttOptions.ApiKey, Is.EqualTo("stt-key-two"));
            Assert.That(sttOptions.BaseUrl, Is.EqualTo("https://stt.invalid/v1"));
            Assert.That(llmOptions.ApiKey, Is.EqualTo("llm-key-one"));
            Assert.That(ttsOptions.ApiKey, Is.EqualTo("tts-key-one"));
            Assert.That(llmOptions.BaseUrl, Is.EqualTo("https://first.invalid/v1"));
            Assert.That(ttsOptions.BaseUrl, Is.EqualTo("https://first.invalid/v1"));

            llm.ApiKey = "llm-key-two";
            llm.BaseUrl = "https://llm.invalid/v1";
            tts.ApiKey = "tts-key-two";
            tts.BaseUrl = "https://tts.invalid/v1";
            llm.NotifyChanged(); tts.NotifyChanged();
            Tick(stt); Tick(llm); Tick(tts);
            await SpeechAsync.Yield();
            Assert.That(sttOptions.ApiKey, Is.EqualTo("stt-key-two"));
            Assert.That(llmOptions.ApiKey, Is.EqualTo("llm-key-two"));
            Assert.That(llmOptions.BaseUrl, Is.EqualTo("https://llm.invalid/v1"));
            Assert.That(ttsOptions.ApiKey, Is.EqualTo("tts-key-two"));
            Assert.That(ttsOptions.BaseUrl, Is.EqualTo("https://tts.invalid/v1"));
        }

        private T Create<T>() where T : LiveSpeechComponent
        {
            var owner = new GameObject("Speech component test");
            objects.Add(owner);
            var component = owner.AddComponent<T>();
            components.Add(component);
            return component;
        }
        private async UniTask<SpeechDetectorLease> CreateDetector(SpeechDetectorComponent component)
        {
            var lease = await component.CreateDetectorAsync(null, CancellationToken.None);
            leases.Add(lease);
            return lease;
        }
        private static void BindDetector(SpeechDetectorComponent component, ISpeechDetector detector)
            => Bind(component, () =>
            {
                var next = component.BuildOptions();
                return token => component.ApplyDetectorOptionsAsync(detector, next, token);
            });
        private static void Bind(LiveSpeechComponent component, Func<Func<CancellationToken, UniTask>> prepare)
            => typeof(LiveSpeechComponent).GetMethod("BindSettings", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, new object[] { prepare });
        private static UniTask Unbind(LiveSpeechComponent component)
            => (UniTask)typeof(LiveSpeechComponent).GetMethod("UnbindSettingsAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        private static void Tick(LiveSpeechComponent component)
            => typeof(LiveSpeechComponent).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(component, null);
        private static SpeechCompletionSource<bool> Signal()
            => new SpeechCompletionSource<bool>();
        private static async UniTask Within(UniTask task)
        {
            task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromSeconds(5), CancellationToken.None)), Is.EqualTo(0), "The operation did not finish.");
            await task;
        }
        private static byte[] Pcm(short amplitude)
        {
            var samples = new byte[1024];
            for (var i = 0; i < samples.Length; i += 2)
            {
                samples[i] = (byte)amplitude;
                samples[i + 1] = (byte)(amplitude >> 8);
            }
            return samples;
        }
    }
}

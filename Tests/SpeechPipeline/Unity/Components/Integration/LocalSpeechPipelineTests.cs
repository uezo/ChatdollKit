using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechPipeline;
using NUnit.Framework;
using UnityEngine;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Integration
{
    public class LocalSpeechPipelineTests
    {
        private GameObject owner;
        private LocalSpeechPipeline pipeline;
        private OfflineRecognizerComponent stt;
        private OfflineLlmComponent llm;
        private OfflineSynthesizerComponent tts;
        private OfflineStreamDetectorComponent vad;
        private readonly List<SpeechPipelineLease> leases = new List<SpeechPipelineLease>();
        private readonly List<SpeechCompletionSource<bool>> gates = new List<SpeechCompletionSource<bool>>();

        [SetUp]
        public void Setup()
        {
            owner = new GameObject("Local pipeline integration test");
            pipeline = owner.AddComponent<LocalSpeechPipeline>();
            stt = owner.AddComponent<OfflineRecognizerComponent>();
            llm = owner.AddComponent<OfflineLlmComponent>();
            tts = owner.AddComponent<OfflineSynthesizerComponent>();
            vad = owner.AddComponent<OfflineStreamDetectorComponent>();
        }
        [TearDown]
        public async NUnitTask Cleanup()
        {
            foreach (var gate in gates) gate.TrySetResult(true);
            foreach (var lease in leases) await lease.DisposeAsync();
            foreach (var detector in vad.Created) await detector.DisposeAsync();
            UnityEngine.Object.DestroyImmediate(owner);
            leases.Clear(); gates.Clear();
        }

        [Test]
        public async NUnitTask LocalLeaseSharesSttBindsLiveOptionsAndReleasesEveryOwnedServiceBeforeRegeneration()
        {
            var first = await Create();
            Assert.That(first.InputSampleRate, Is.EqualTo(16000));
            Assert.That(first.SamplesPerMessage, Is.EqualTo(512));
            Assert.That(((SileroStreamSpeechDetectorEngine)vad.Detector).SpeechRecognizer, Is.SameAs(stt.Recognizer));
            AssertBound(true);
            llm.Model = "offline-two";
            await llm.ApplySettingsAsync();
            Assert.That(llm.Created[0].GetOptions().Model, Is.EqualTo("offline-two"));
            var core = new ChatdollOrchestratorEngine(first.Pipeline, new OfflineAvatar());
            try
            {
                await first.StopSettingsUpdatesAsync();
                AssertBound(false);
                AssertReferencesCleared();
                Assert.That(stt.Created[0].Disposals, Is.Zero);
                await core.DisposeAsync();
                await first.DisposeAsync();
                await first.DisposeAsync();
                AssertDisposed(0);
                var second = await Create();
                Assert.That(second.Pipeline, Is.Not.SameAs(first.Pipeline));
                Assert.That(stt.Recognizer, Is.SameAs(stt.Created[1]));
                Assert.That(((SileroStreamSpeechDetectorEngine)vad.Detector).SpeechRecognizer, Is.SameAs(stt.Created[1]));
                AssertBound(true);
                await second.DisposeAsync();
                AssertDisposed(1);
            }
            finally { await core.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask DuplicateProvidersRequireExplicitSelectionBeforeCreatingServices()
        {
            var duplicate = owner.AddComponent<OfflineRecognizerComponent>();
            await ExpectExceptionAsync<InvalidOperationException>(async () => await pipeline.CreatePipelineAsync(CancellationToken.None));
            Assert.That(stt.Created, Is.Empty);
            Assert.That(duplicate.Created, Is.Empty);
            pipeline.Stt = stt;
            await Create();
            Assert.That(stt.Recognizer, Is.Not.Null);
            Assert.That(duplicate.Recognizer, Is.Null);
            Assert.That(duplicate.Created, Is.Empty);
        }

        [Test]
        public async NUnitTask PipelineCreatedSubscriberFailureDisposesAllResourcesAndClearsPublishedReferences()
        {
            pipeline.PipelineCreated += created => throw new InvalidOperationException("intentional subscriber failure");
            await ExpectExceptionAsync<InvalidOperationException>(async () => await pipeline.CreatePipelineAsync(CancellationToken.None));
            AssertBound(false);
            AssertReferencesCleared();
            AssertDisposed(0);
        }

        [Test]
        public async NUnitTask CancellationDuringModelLoadingDisposesItsLateResultWithoutPublishingBindings()
        {
            var gate = Gate(); vad.NextCreationGate = gate;
            using (var cancellation = new CancellationTokenSource())
            {
                var creating = pipeline.CreatePipelineAsync(cancellation.Token);
                Assert.That(creating.Status.IsCompleted(), Is.False);
                cancellation.Cancel();
                gate.TrySetResult(true);
                await ExpectFailure<OperationCanceledException>(creating);
                AssertBound(false);
                AssertReferencesCleared();
                AssertDisposed(0);
            }
        }

        [Test]
        public async NUnitTask ConcurrentStartupCannotClearTheReferencesPublishedByTheWinningStartup()
        {
            var gate = Gate(); vad.NextCreationGate = gate;
            var first = pipeline.CreatePipelineAsync(CancellationToken.None);
            Assert.That(first.Status.IsCompleted(), Is.False);
            var winner = await Create();
            var recognizer = stt.Recognizer; var detector = vad.Detector;
            var service = llm.Service; var synthesizer = tts.Synthesizer;
            var conversation = pipeline.Conversation;
            gate.TrySetResult(true);
            await ExpectFailure<InvalidOperationException>(first);
            Assert.That(stt.Recognizer, Is.SameAs(recognizer));
            Assert.That(vad.Detector, Is.SameAs(detector));
            Assert.That(llm.Service, Is.SameAs(service));
            Assert.That(tts.Synthesizer, Is.SameAs(synthesizer));
            Assert.That(pipeline.Conversation, Is.SameAs(conversation));
            Assert.That(stt.Created[0].Disposals, Is.EqualTo(1));
            Assert.That(stt.Created[1].Disposals, Is.Zero);
            AssertBound(true);
            await winner.DisposeAsync();
            Assert.That(stt.Created[1].Disposals, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask LiveInspectorEditDuringModelLoadingIsAppliedAfterBindingTheOriginalSnapshot()
        {
            var gate = Gate(); vad.NextCreationGate = gate;
            var creating = pipeline.CreatePipelineAsync(CancellationToken.None);
            llm.Model = "edited-during-model-load";
            llm.NotifyChanged();
            gate.TrySetResult(true);
            var lease = await creating; leases.Add(lease);
            Assert.That(llm.Created[0].GetOptions().Model, Is.EqualTo("offline-one"));
            Tick(llm);
            await SpeechAsync.Yield();
            Assert.That(llm.Created[0].GetOptions().Model, Is.EqualTo("edited-during-model-load"));
            Assert.That(llm.SettingsError, Is.Null);
        }

        [Test]
        public async NUnitTask ProviderSelectionEditDuringModelLoadingKeepsTheAcquiredProviderAndRequiresRestart()
        {
            var gate = Gate(); vad.NextCreationGate = gate;
            var creating = pipeline.CreatePipelineAsync(CancellationToken.None);
            var replacement = owner.AddComponent<OfflineRecognizerComponent>();
            pipeline.Stt = replacement;
            pipeline.NotifyChanged();
            gate.TrySetResult(true);
            var lease = await creating; leases.Add(lease);
            Tick(pipeline);
            Assert.That(pipeline.NeedsRestart, Is.True);
            Assert.That(stt.Recognizer, Is.SameAs(stt.Created[0]));
            Assert.That(replacement.Recognizer, Is.Null);
            Assert.That(((SileroStreamSpeechDetectorEngine)vad.Detector).SpeechRecognizer, Is.SameAs(stt.Recognizer));
        }

        [Test]
        public async NUnitTask DisablingASelectedProviderDuringModelLoadingRejectsStartupAndCleansUp()
        {
            var gate = Gate(); vad.NextCreationGate = gate;
            var creating = pipeline.CreatePipelineAsync(CancellationToken.None);
            llm.enabled = false;
            gate.TrySetResult(true);
            await ExpectFailure<OperationCanceledException>(creating);
            AssertReferencesCleared();
            AssertDisposed(0);
        }

        private async UniTask<SpeechPipelineLease> Create()
        { var lease = await pipeline.CreatePipelineAsync(CancellationToken.None); leases.Add(lease); return lease; }
        private SpeechCompletionSource<bool> Gate()
        { var gate = new SpeechCompletionSource<bool>(); gates.Add(gate); return gate; }
        private void AssertBound(bool bound)
        {
            foreach (var item in new LiveSpeechComponent[] { pipeline, stt, llm, tts, vad })
                Assert.That(item.IsBound, Is.EqualTo(bound), item.GetType().Name);
        }
        private void AssertReferencesCleared()
        {
            Assert.That(stt.Recognizer, Is.Null); Assert.That(llm.Service, Is.Null);
            Assert.That(tts.Synthesizer, Is.Null); Assert.That(vad.Detector, Is.Null);
            Assert.That(pipeline.Conversation, Is.Null);
        }
        private void AssertDisposed(int index)
        {
            Assert.That(stt.Created[index].Disposals, Is.EqualTo(1));
            Assert.That(llm.Created[index].Disposals, Is.EqualTo(1));
            Assert.That(tts.Created[index].Disposals, Is.EqualTo(1));
            Assert.That(vad.Models[index].Disposals, Is.EqualTo(1));
        }
        private static async UniTask ExpectFailure<T>(UniTask task) where T : Exception
        {
            using (var timeout = new CancellationTokenSource())
            using (SpeechAsync.Timeout(timeout, TimeSpan.FromSeconds(5)))
            {
                try { await SpeechAsync.WaitAsync(task, timeout.Token); Assert.Fail("Expected " + typeof(T).Name); }
                catch (T) when (!timeout.IsCancellationRequested) { }
            }
        }
        private static void Tick(LiveSpeechComponent component)
            => typeof(LiveSpeechComponent).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(component, null);
        private static async UniTask<T> ExpectExceptionAsync<T>(Func<UniTask> operation) where T : Exception
        {
            try { await operation(); }
            catch (Exception error)
            {
                Assert.That(error, Is.InstanceOf<T>());
                return (T)error;
            }
            Assert.Fail("Expected " + typeof(T).Name);
            return null;
        }

    }
}

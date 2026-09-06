using ChatdollKit.SpeechPipeline.VAD.Silero;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechPipeline;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Integration
{
    public class InspectorPipelineOrchestratorTests
    {
        private GameObject owner;
        private ChatdollOrchestrator orchestrator;
        private LocalSpeechPipeline pipeline;
        private OfflineRecognizerComponent stt;
        private OfflineLlmComponent llm;
        private OfflineSynthesizerComponent tts;
        private OfflineStreamDetectorComponent vad;
        private OfflineAvatar avatar;
        private readonly List<SpeechCompletionSource<bool>> gates = new List<SpeechCompletionSource<bool>>();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return new EnterPlayMode();
            owner = new GameObject("Inspector pipeline orchestrator integration test");
            pipeline = owner.AddComponent<LocalSpeechPipeline>();
            stt = owner.AddComponent<OfflineRecognizerComponent>();
            llm = owner.AddComponent<OfflineLlmComponent>();
            tts = owner.AddComponent<OfflineSynthesizerComponent>();
            vad = owner.AddComponent<OfflineStreamDetectorComponent>();
            orchestrator = owner.AddComponent<ChatdollOrchestrator>();
            orchestrator.PipelineComponent = pipeline;
            avatar = new OfflineAvatar();
            orchestrator.ConfigureAvatar(avatar);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var gate in gates) gate.TrySetResult(true);
            gates.Clear();
            if (orchestrator != null) yield return Wait(orchestrator.StopAsync());
            if (owner != null) UnityEngine.Object.Destroy(owner);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator OrchestratorStartsTheInspectorPipelineHandlesTextAndCreatesFreshServicesAfterStop()
        {
            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.IsRunning, Is.True);
            Assert.That(pipeline.IsBound, Is.True);
            Assert.That(((SileroStreamSpeechDetectorEngine)vad.Detector).SpeechRecognizer, Is.SameAs(stt.Recognizer));
            var first = orchestrator.Pipeline;
            yield return Wait(orchestrator.SendTextAsync("offline input"));
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(llm.Created[0].Calls, Is.EqualTo(1));
            Assert.That(tts.Created[0].Calls, Is.EqualTo(1));
            Assert.That(avatar.Presentations, Is.EqualTo(1));
            yield return Wait(orchestrator.StopAsync());
            Assert.That(orchestrator.IsRunning, Is.False);
            Assert.That(stt.Recognizer, Is.Null);
            Assert.That(vad.Detector, Is.Null);
            Assert.That(pipeline.IsBound, Is.False);
            Assert.That(stt.Created[0].Disposals, Is.EqualTo(1));
            Assert.That(llm.Created[0].Disposals, Is.EqualTo(1));
            Assert.That(tts.Created[0].Disposals, Is.EqualTo(1));
            Assert.That(vad.Models[0].Disposals, Is.EqualTo(1));
            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.Pipeline, Is.Not.SameAs(first));
            Assert.That(stt.Created.Count, Is.EqualTo(2));
            yield return Wait(orchestrator.StopAsync());
            Assert.That(stt.Created[1].Disposals, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OrchestratorStopCancelsInspectorStartupAndDisposesTheLateDetectorBeforeRestart()
        {
            var release = Signal(); gates.Add(release);
            vad.NextCreationGate = release;
            vad.CreationEntered = Signal();
            var starting = orchestrator.StartAsync();
            yield return Wait(vad.CreationEntered.Task);
            var stopping = orchestrator.StopAsync();
            Assert.That(vad.CreationToken.IsCancellationRequested, Is.True);
            Assert.That(stopping.Status.IsCompleted(), Is.False);
            release.TrySetResult(true);
            yield return Wait(stopping);
            Assert.That(starting.Status.IsCanceled(), Is.True);
            Assert.That(orchestrator.IsRunning, Is.False);
            Assert.That(pipeline.IsBound, Is.False);
            Assert.That(stt.Recognizer, Is.Null);
            Assert.That(stt.Created[0].Disposals, Is.EqualTo(1));
            Assert.That(llm.Created[0].Disposals, Is.EqualTo(1));
            Assert.That(tts.Created[0].Disposals, Is.EqualTo(1));
            Assert.That(vad.Models[0].Disposals, Is.EqualTo(1));
            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.IsRunning, Is.True);
            Assert.That(stt.Recognizer, Is.SameAs(stt.Created[1]));
        }

        private static SpeechCompletionSource<bool> Signal()
            => new SpeechCompletionSource<bool>();
        private static IEnumerator Wait(UniTask task)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, "Orchestrator operation did not finish.");
            if (task.Status.IsFaulted()) task.GetAwaiter().GetResult();
            Assert.That(task.Status.IsCanceled(), Is.False, "Unexpected cancellation.");
        }
    }
}
